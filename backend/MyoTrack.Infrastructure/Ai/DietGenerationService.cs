using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyoTrack.Domain;
using MyoTrack.Domain.Entities;
using MyoTrack.Domain.Services;

namespace MyoTrack.Infrastructure.Ai;

/// <summary>
/// TDEE e macros são calculados deterministicamente (nunca pelo LLM).
/// O LLM apenas monta refeições com itens do catálogo; o backend recalcula
/// os totais e ajusta as quantidades para bater as metas.
/// </summary>
public class DietGenerationService(
    AppDbContext db,
    ILlmJsonClient llm,
    ILogger<DietGenerationService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> GenerateAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await db.UserProfiles.SingleOrDefaultAsync(p => p.UserId == userId, ct)
            ?? throw new InvalidOperationException("Perfil não encontrado. Complete o onboarding antes de gerar a dieta.");

        if (profile.BirthDate is null || profile.Sex is null || profile.HeightCm is null)
            throw new InvalidOperationException("Perfil incompleto: data de nascimento, sexo e altura são necessários para calcular as metas.");

        var latestWeight = await db.BodyMeasurements
            .Where(m => m.UserId == userId && m.WeightKg != null)
            .OrderByDescending(m => m.Date)
            .Select(m => m.WeightKg)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Registre seu peso corporal antes de gerar a dieta.");

        var calorieGoal = profile.Goal switch
        {
            FitnessGoal.WeightLoss => CalorieGoal.Deficit,
            FitnessGoal.Hypertrophy => CalorieGoal.Surplus,
            _ => CalorieGoal.Maintenance,
        };

        var age = TdeeCalculator.CalculateAge(profile.BirthDate.Value, DateOnly.FromDateTime(DateTime.UtcNow));
        var targets = TdeeCalculator.CalculateTargets(
            profile.Sex, latestWeight, profile.HeightCm.Value, age,
            profile.TrainingDaysPerWeek, calorieGoal);

        var catalog = await db.FoodItems.AsNoTracking().ToListAsync(ct);
        var foodsById = catalog.ToDictionary(f => f.Id);

        var diet = DietRuleEngine.Generate(targets, catalog, profile.DietaryRestrictions);
        string? rawLlmOutput = null;

        if (llm.IsConfigured)
        {
            rawLlmOutput = await AssembleWithLlmAsync(profile, targets, catalog, ct);
            if (rawLlmOutput is not null && TryParseAndValidate(rawLlmOutput, catalog, profile.DietaryRestrictions, out var llmDiet))
                diet = AdjustQuantities(llmDiet, targets, foodsById);
            else if (rawLlmOutput is not null)
                logger.LogWarning("Saída do LLM inválida para o usuário {UserId}; usando dieta por regras.", userId);
        }

        var previous = await db.DietPlans
            .Where(p => p.UserId == userId && p.Status == PlanStatus.Active)
            .ToListAsync(ct);
        foreach (var old in previous)
            old.Status = PlanStatus.Archived;

        var version = await db.DietPlans.CountAsync(p => p.UserId == userId, ct) + 1;
        var entity = new DietPlan
        {
            UserId = userId,
            Name = $"Dieta v{version}",
            CalorieGoal = calorieGoal,
            Status = PlanStatus.Active,
            Version = version,
            TargetKcal = targets.Kcal,
            TargetProteinG = targets.ProteinG,
            TargetCarbsG = targets.CarbsG,
            TargetFatG = targets.FatG,
            GenerationInputJson = JsonSerializer.Serialize(new { targets, calorieGoal = calorieGoal.ToString() }, JsonOptions),
            RawLlmOutputJson = rawLlmOutput,
            Meals = diet.Meals.Select(m => new Meal
            {
                Order = m.Order,
                Name = m.Name,
                Items = m.Items.Select(i => new MealItem
                {
                    FoodItemId = i.FoodItemId,
                    QuantityG = i.QuantityG,
                }).ToList(),
            }).ToList(),
        };

        db.DietPlans.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }

    private async Task<string?> AssembleWithLlmAsync(
        UserProfile profile, MacroTargets targets, List<FoodItem> catalog, CancellationToken ct)
    {
        var system = """
            Você é um nutricionista. Monte um plano alimentar de um dia com 4 a 6 refeições usando
            SOMENTE alimentos da lista fornecida (foodItemId). Respeite restrições e preferências do usuário.
            Aproxime-se das metas de calorias e macros; quantidades em gramas entre 10 e 500.
            Nomes de refeições em português (ex.: "Café da manhã", "Almoço").
            """;

        var user = JsonSerializer.Serialize(new
        {
            metas = new { kcal = targets.Kcal, proteinaG = targets.ProteinG, carboidratoG = targets.CarbsG, gorduraG = targets.FatG },
            restricoes = profile.DietaryRestrictions,
            preferencias = profile.FoodPreferences,
            alimentos = catalog.Select(f => new
            {
                foodItemId = f.Id,
                nome = f.Name,
                kcal100g = f.KcalPer100g,
                proteina100g = f.ProteinPer100g,
                carbo100g = f.CarbsPer100g,
                gordura100g = f.FatPer100g,
            }),
        }, JsonOptions);

        return await llm.GenerateJsonAsync(system, user, DietSchema(), ct);
    }

    private static bool TryParseAndValidate(
        string json, List<FoodItem> catalog, List<string> restrictions, out GeneratedDiet result)
    {
        result = new GeneratedDiet([]);
        try
        {
            var parsed = JsonSerializer.Deserialize<LlmDiet>(json, JsonOptions);
            if (parsed?.Meals is null || parsed.Meals.Count is < 3 or > 6)
                return false;

            var foodsById = catalog.ToDictionary(f => f.Id);
            var meals = new List<GeneratedMeal>();
            foreach (var (meal, index) in parsed.Meals.OrderBy(m => m.Order).Select((m, i) => (m, i)))
            {
                if (meal.Items is null || meal.Items.Count == 0) return false;

                var items = new List<GeneratedMealItem>();
                foreach (var item in meal.Items)
                {
                    if (!foodsById.TryGetValue(item.FoodItemId, out var food)) return false;
                    if (item.QuantityG is < 10 or > 500) return false;
                    if (restrictions.Any(r => food.Name.Contains(r, StringComparison.OrdinalIgnoreCase))) return false;

                    items.Add(new GeneratedMealItem(food.Id, food.Name, item.QuantityG));
                }
                meals.Add(new GeneratedMeal(index + 1, meal.Name ?? $"Refeição {index + 1}", items));
            }

            result = new GeneratedDiet(meals);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Escala as quantidades para aproximar as kcal da meta (o LLM sugere, o código garante os números).</summary>
    private static GeneratedDiet AdjustQuantities(
        GeneratedDiet diet, MacroTargets targets, IReadOnlyDictionary<int, FoodItem> foodsById)
    {
        var totals = DietRuleEngine.Totals(diet, foodsById);
        if (totals.Kcal <= 0) return diet;

        var factor = Math.Clamp(targets.Kcal / totals.Kcal, 0.5m, 2.0m);
        if (Math.Abs(factor - 1m) < 0.05m) return diet;

        var meals = diet.Meals.Select(m => new GeneratedMeal(
            m.Order, m.Name,
            m.Items.Select(i => i with
            {
                QuantityG = Math.Clamp(Math.Round(i.QuantityG * factor / 5m) * 5m, 10m, 500m),
            }).ToList())).ToList();

        return new GeneratedDiet(meals);
    }

    private static Dictionary<string, JsonElement> DietSchema()
    {
        const string schema = """
            {
              "type": "object",
              "properties": {
                "meals": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "order": { "type": "integer" },
                      "name": { "type": "string" },
                      "items": {
                        "type": "array",
                        "items": {
                          "type": "object",
                          "properties": {
                            "foodItemId": { "type": "integer" },
                            "quantityG": { "type": "number" }
                          },
                          "required": ["foodItemId", "quantityG"],
                          "additionalProperties": false
                        }
                      }
                    },
                    "required": ["order", "name", "items"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["meals"],
              "additionalProperties": false
            }
            """;
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(schema)!;
    }

    private sealed record LlmDiet(List<LlmMeal> Meals);
    private sealed record LlmMeal(int Order, string? Name, List<LlmMealItem> Items);
    private sealed record LlmMealItem(int FoodItemId, decimal QuantityG);
}
