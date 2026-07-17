using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
public class MealAnalysisService(
    AppDbContext db,
    IMediaStorage storage,
    ILlmJsonClient llm,
    GeminiImageClient imageClient,
    ILogger<MealAnalysisService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record MealJobInput(bool Illustrated);

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
            Informe também posX e posY: a posição aproximada do centro do alimento na imagem,
            em uma escala de 0 a 1000 (0,0 = canto superior esquerdo; 1000,1000 = canto inferior direito).
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

        // Null do cliente = falha de infraestrutura (rate limit, 5xx, resposta
        // truncada) — transiente, o poller reprocessa; não é erro de negócio.
        var result = await llm.GenerateJsonFromImageAsync(system, user, imageBytes, mediaType, MealSchema(), ct)
            ?? throw new TransientAiException("A análise da imagem falhou. Tente novamente.");

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
        var filtered = parsed.Items
            .Where(i => i.QuantityG is > 0 and <= 2000)
            .ToList();
        var items = filtered
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

        var input = string.IsNullOrEmpty(job.InputJson)
            ? null
            : JsonSerializer.Deserialize<MealJobInput>(job.InputJson, JsonOptions);
        if (input is { Illustrated: true })
            analysis.IllustratedMediaKey =
                await GenerateIllustratedAsync(job, imageBytes, mediaType, items, filtered, analysis, ct);

        db.MealPhotoAnalyses.Add(analysis);
        await db.SaveChangesAsync(ct);
        return analysis.Id;
    }

    /// <summary>
    /// Gera a versão ilustrada (anotações de itens e macros na própria foto).
    /// Primeiro tenta o modelo de imagem do Gemini (requer billing); sem
    /// cota/chave ou em falha, cai no renderizador local (SkiaSharp) usando as
    /// posições que o modelo de visão devolveu — nunca falha o job por isso.
    /// </summary>
    private async Task<string?> GenerateIllustratedAsync(
        AnalysisJob job, byte[] imageBytes, string mediaType,
        IReadOnlyList<MealItemDto> items, IReadOnlyList<LlmMealItem> parsedItems,
        MealPhotoAnalysis analysis, CancellationToken ct)
    {
        var labels = items.Select(i =>
            $"{i.Description} — {Math.Round(i.QuantityG)} g · {Math.Round(i.KcalPer100g * i.QuantityG / 100m)} kcal").ToList();
        var totals =
            $"{Math.Round(analysis.TotalKcal)} kcal · P {Math.Round(analysis.TotalProteinG)} g · C {Math.Round(analysis.TotalCarbsG)} g · G {Math.Round(analysis.TotalFatG)} g";

        var generated = await TryGeminiImageAsync(job, labels, totals, imageBytes, mediaType, ct);
        if (generated is null)
        {
            // Fallback local e gratuito: desenha as etiquetas nas posições
            // (posX/posY, escala 0-1000) apontadas pelo modelo de visão.
            var annotations = items
                .Select((item, index) => new MealAnnotation(
                    labels[index],
                    parsedItems[index].PosX is > 0 and <= 1000 ? parsedItems[index].PosX : null,
                    parsedItems[index].PosY is > 0 and <= 1000 ? parsedItems[index].PosY : null))
                .ToList();
            try
            {
                generated = new GeneratedImage(
                    MealImageAnnotator.Render(imageBytes, annotations, totals), "image/jpeg", 0, 0);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Renderização local da análise ilustrada falhou para o job {JobId}.", job.Id);
                return null;
            }
        }

        var extension = generated.MediaType == "image/jpeg" ? ".jpg" : ".png";
        var key = $"meals/{job.UserId}/{Guid.NewGuid():N}-ilustrada{extension}";
        using var stream = new MemoryStream(generated.Bytes);
        await storage.UploadAsync(key, stream, generated.MediaType);
        return key;
    }

    private async Task<GeneratedImage?> TryGeminiImageAsync(
        AnalysisJob job, IReadOnlyList<string> labels, string totals,
        byte[] imageBytes, string mediaType, CancellationToken ct)
    {
        var lines = string.Join("\n", labels.Select(l => $"- {l}"));
        var instruction = $"""
            Edite esta foto de refeição adicionando anotações visuais elegantes e legíveis por cima da imagem,
            como em um infográfico de nutrição. Mantenha a foto original como fundo, sem alterar a comida.
            Para cada item abaixo, desenhe uma etiqueta discreta apontando para o alimento correspondente:
            {lines}
            Adicione também um cartão de resumo em um canto com os totais: {totals}
            Todos os textos em português.
            """;

        var generated = await imageClient.EditImageAsync(imageBytes, mediaType, instruction, ct);
        if (generated is null)
            return null;

        db.AiUsageLogs.Add(new AiUsageLog
        {
            UserId = job.UserId,
            Operation = AnalysisJobType.MealPhoto,
            Model = imageClient.Model,
            InputTokens = generated.InputTokens,
            OutputTokens = generated.OutputTokens,
        });
        return generated;
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
                      "fatPer100g": { "type": "number" },
                      "posX": { "type": "integer", "description": "Centro do alimento na imagem, eixo X em escala 0-1000" },
                      "posY": { "type": "integer", "description": "Centro do alimento na imagem, eixo Y em escala 0-1000" }
                    },
                    "required": ["description", "foodItemId", "quantityG", "kcalPer100g", "proteinPer100g", "carbsPer100g", "fatPer100g", "posX", "posY"],
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
        decimal KcalPer100g, decimal ProteinPer100g, decimal CarbsPer100g, decimal FatPer100g,
        int? PosX = null, int? PosY = null);
}
