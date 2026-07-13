using MyoTrack.Domain.Entities;

namespace MyoTrack.Domain.Services;

public record GeneratedMealItem(int FoodItemId, string Name, decimal QuantityG);
public record GeneratedMeal(int Order, string Name, List<GeneratedMealItem> Items);
public record GeneratedDiet(List<GeneratedMeal> Meals);

/// <summary>
/// Montagem determinística de plano alimentar a partir do catálogo, usada como
/// fallback sem LLM e como base que o LLM apenas re-arranja (trocas por preferência).
/// Classifica alimentos pelo macro dominante e escala quantidades para bater as metas.
/// </summary>
public static class DietRuleEngine
{
    private static bool IsProteinSource(FoodItem f) =>
        f.ProteinPer100g >= 10m && f.ProteinPer100g * 4m >= f.KcalPer100g * 0.4m;

    private static bool IsCarbSource(FoodItem f) =>
        f.CarbsPer100g >= 15m && f.FatPer100g < 10m && !IsProteinSource(f);

    private static bool IsFatSource(FoodItem f) => f.FatPer100g >= 20m;

    private static bool IsVegetable(FoodItem f) =>
        f.KcalPer100g <= 40m && f.CarbsPer100g < 10m;

    public static GeneratedDiet Generate(MacroTargets targets, IReadOnlyList<FoodItem> catalog, List<string> restrictions)
    {
        var allowed = catalog
            .Where(f => !restrictions.Any(r => f.Name.Contains(r, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var proteins = allowed.Where(IsProteinSource).OrderByDescending(f => f.ProteinPer100g).ToList();
        var carbs = allowed.Where(IsCarbSource).OrderBy(f => f.FatPer100g).ToList();
        var fats = allowed.Where(IsFatSource).OrderByDescending(f => f.FatPer100g).ToList();
        var vegetables = allowed.Where(IsVegetable).ToList();

        if (proteins.Count == 0 || carbs.Count == 0)
            throw new InvalidOperationException("Catálogo insuficiente para montar a dieta com as restrições informadas.");

        string[] mealNames = ["Café da manhã", "Almoço", "Lanche da tarde", "Jantar"];
        // Distribuição das metas por refeição: 20% / 35% / 15% / 30%.
        decimal[] shares = [0.20m, 0.35m, 0.15m, 0.30m];

        var meals = new List<GeneratedMeal>();
        for (var i = 0; i < mealNames.Length; i++)
        {
            var items = new List<GeneratedMealItem>();

            var protein = proteins[i % proteins.Count];
            var carb = carbs[i % carbs.Count];
            var fat = fats.Count > 0 ? fats[i % fats.Count] : null;

            // Gordura embutida nas fontes de proteína/carbo conta pouco aqui; a fonte
            // dedicada cobre ~60% da meta de gordura da refeição para não estourar kcal.
            var proteinQty = Quantity(targets.ProteinG * shares[i], protein.ProteinPer100g);
            var carbQty = Quantity(targets.CarbsG * shares[i], carb.CarbsPer100g);

            items.Add(new GeneratedMealItem(protein.Id, protein.Name, proteinQty));
            items.Add(new GeneratedMealItem(carb.Id, carb.Name, carbQty));

            if (fat is not null)
                items.Add(new GeneratedMealItem(fat.Id, fat.Name, Quantity(targets.FatG * shares[i] * 0.6m, fat.FatPer100g)));

            // Vegetais nas refeições principais.
            if (vegetables.Count > 0 && (i == 1 || i == 3))
                items.Add(new GeneratedMealItem(vegetables[i % vegetables.Count].Id, vegetables[i % vegetables.Count].Name, 100m));

            meals.Add(new GeneratedMeal(i + 1, mealNames[i], items));
        }

        return new GeneratedDiet(meals);
    }

    /// <summary>Gramas necessárias para atingir a meta do macro, em múltiplos de 5 g (mín. 10 g, máx. 500 g).</summary>
    private static decimal Quantity(decimal targetMacroG, decimal macroPer100g)
    {
        if (macroPer100g <= 0m) return 0m;
        var grams = targetMacroG / macroPer100g * 100m;
        return Math.Clamp(Math.Round(grams / 5m) * 5m, 10m, 500m);
    }

    public static (decimal Kcal, decimal ProteinG, decimal CarbsG, decimal FatG) Totals(
        GeneratedDiet diet, IReadOnlyDictionary<int, FoodItem> foodsById)
    {
        decimal kcal = 0, protein = 0, carbs = 0, fat = 0;
        foreach (var item in diet.Meals.SelectMany(m => m.Items))
        {
            var food = foodsById[item.FoodItemId];
            var factor = item.QuantityG / 100m;
            kcal += food.KcalPer100g * factor;
            protein += food.ProteinPer100g * factor;
            carbs += food.CarbsPer100g * factor;
            fat += food.FatPer100g * factor;
        }
        return (Math.Round(kcal), Math.Round(protein), Math.Round(carbs), Math.Round(fat));
    }
}
