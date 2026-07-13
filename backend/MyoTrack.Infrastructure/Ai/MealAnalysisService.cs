using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Domain;
using MyoTrack.Domain.Entities;
using MyoTrack.Infrastructure.Storage;

namespace MyoTrack.Infrastructure.Ai;

/// <summary>Item detectado na foto. Valores por 100 g vêm do catálogo TACO quando há correspondência.</summary>
public record MealItemDto(
    string Description,
    int? FoodItemId,
    decimal QuantityG,
    decimal KcalPer100g,
    decimal ProteinPer100g,
    decimal CarbsPer100g,
    decimal FatPer100g);

/// <summary>
/// Análise de refeição por foto: LLM multimodal identifica alimentos e porções;
/// o backend cruza com o catálogo TACO para os macros oficiais sempre que possível.
/// </summary>
public class MealAnalysisService(AppDbContext db, IMediaStorage storage, ILlmJsonClient llm)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> AnalyzeAsync(AnalysisJob job, CancellationToken ct = default)
    {
        if (!llm.IsConfigured)
            throw new InvalidOperationException("Análise de refeição indisponível: chave da API de IA não configurada no servidor.");
        if (string.IsNullOrEmpty(job.MediaKey))
            throw new InvalidOperationException("Job sem imagem associada.");

        var imageBytes = await storage.DownloadAsync(job.MediaKey, ct);
        var mediaType = MediaTypeFromKey(job.MediaKey);
        var catalog = await db.FoodItems.AsNoTracking().ToListAsync(ct);

        var system = """
            Você é um nutricionista analisando a foto de um prato de comida.
            Identifique cada alimento visível e estime a porção em gramas.
            Para cada item, procure o alimento correspondente na lista de catálogo fornecida e
            informe seu foodItemId; se não houver correspondência razoável, use foodItemId = 0 e
            preencha os valores nutricionais por 100 g com sua melhor estimativa.
            Descrições em português. Se a imagem não contiver comida, retorne a lista vazia.
            """;

        var user = JsonSerializer.Serialize(new
        {
            catalogo = catalog.Select(f => new
            {
                foodItemId = f.Id,
                nome = f.Name,
                kcal100g = f.KcalPer100g,
                proteina100g = f.ProteinPer100g,
                carbo100g = f.CarbsPer100g,
                gordura100g = f.FatPer100g,
            }),
        }, JsonOptions);

        var result = await llm.GenerateJsonFromImageAsync(system, user, imageBytes, mediaType, MealSchema(), ct)
            ?? throw new InvalidOperationException("A análise da imagem falhou. Tente novamente.");

        db.AiUsageLogs.Add(new AiUsageLog
        {
            UserId = job.UserId,
            Operation = AnalysisJobType.MealPhoto,
            Model = llm.Model,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
        });

        var parsed = JsonSerializer.Deserialize<LlmMealAnalysis>(result.Json, JsonOptions)
            ?? throw new InvalidOperationException("Resposta inválida da análise.");
        if (parsed.Items.Count == 0)
            throw new InvalidOperationException("Nenhum alimento identificado na imagem. Envie uma foto nítida do prato.");

        var foodsById = catalog.ToDictionary(f => f.Id);
        var items = parsed.Items
            .Where(i => i.QuantityG is > 0 and <= 2000)
            .Select(i =>
            {
                // Catálogo é a fonte oficial de macros quando há correspondência.
                if (i.FoodItemId > 0 && foodsById.TryGetValue(i.FoodItemId, out var food))
                    return new MealItemDto(food.Name, food.Id, Math.Round(i.QuantityG),
                        food.KcalPer100g, food.ProteinPer100g, food.CarbsPer100g, food.FatPer100g);

                return new MealItemDto(i.Description, null, Math.Round(i.QuantityG),
                    Math.Clamp(i.KcalPer100g, 0, 900),
                    Math.Clamp(i.ProteinPer100g, 0, 100),
                    Math.Clamp(i.CarbsPer100g, 0, 100),
                    Math.Clamp(i.FatPer100g, 0, 100));
            })
            .ToList();

        var analysis = new MealPhotoAnalysis
        {
            UserId = job.UserId,
            AnalysisJobId = job.Id,
            MediaKey = job.MediaKey,
            ItemsJson = JsonSerializer.Serialize(items, JsonOptions),
        };
        ApplyTotals(analysis, items);

        db.MealPhotoAnalyses.Add(analysis);
        await db.SaveChangesAsync(ct);
        return analysis.Id;
    }

    public static void ApplyTotals(MealPhotoAnalysis analysis, IReadOnlyList<MealItemDto> items)
    {
        analysis.TotalKcal = Math.Round(items.Sum(i => i.KcalPer100g * i.QuantityG / 100m), 0);
        analysis.TotalProteinG = Math.Round(items.Sum(i => i.ProteinPer100g * i.QuantityG / 100m), 1);
        analysis.TotalCarbsG = Math.Round(items.Sum(i => i.CarbsPer100g * i.QuantityG / 100m), 1);
        analysis.TotalFatG = Math.Round(items.Sum(i => i.FatPer100g * i.QuantityG / 100m), 1);
    }

    private static string MediaTypeFromKey(string key) => Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "image/jpeg",
    };

    private static Dictionary<string, JsonElement> MealSchema()
    {
        const string schema = """
            {
              "type": "object",
              "properties": {
                "items": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "description": { "type": "string" },
                      "foodItemId": { "type": "integer", "description": "Id do catálogo ou 0 se não houver correspondência" },
                      "quantityG": { "type": "number" },
                      "kcalPer100g": { "type": "number" },
                      "proteinPer100g": { "type": "number" },
                      "carbsPer100g": { "type": "number" },
                      "fatPer100g": { "type": "number" }
                    },
                    "required": ["description", "foodItemId", "quantityG", "kcalPer100g", "proteinPer100g", "carbsPer100g", "fatPer100g"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["items"],
              "additionalProperties": false
            }
            """;
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(schema)!;
    }

    private sealed record LlmMealAnalysis(List<LlmMealItem> Items);
    private sealed record LlmMealItem(
        string Description, int FoodItemId, decimal QuantityG,
        decimal KcalPer100g, decimal ProteinPer100g, decimal CarbsPer100g, decimal FatPer100g);
}
