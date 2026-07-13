using MyoTrack.Domain.Entities;
using MyoTrack.Domain.Services;
using MyoTrack.Infrastructure.Seed;

namespace MyoTrack.Tests;

public class DietRuleEngineTests
{
    private static IReadOnlyList<FoodItem> Catalog() =>
        FoodSeed.Items.Select((f, i) => new FoodItem
        {
            Id = i + 1,
            Name = f.Name,
            KcalPer100g = f.KcalPer100g,
            ProteinPer100g = f.ProteinPer100g,
            CarbsPer100g = f.CarbsPer100g,
            FatPer100g = f.FatPer100g,
            FiberPer100g = f.FiberPer100g,
            Source = f.Source,
        }).ToList();

    private static readonly MacroTargets Targets = new(2500m, 160m, 300m, 70m);

    [Fact]
    public void Generates4MealsWithItems()
    {
        var diet = DietRuleEngine.Generate(Targets, Catalog(), []);
        Assert.Equal(4, diet.Meals.Count);
        Assert.All(diet.Meals, m => Assert.NotEmpty(m.Items));
    }

    [Fact]
    public void TotalsApproximateTargets()
    {
        var catalog = Catalog();
        var diet = DietRuleEngine.Generate(Targets, catalog, []);
        var totals = DietRuleEngine.Totals(diet, catalog.ToDictionary(f => f.Id));

        // Montagem por regras simples: tolerância de ±25% nos macros principais.
        Assert.InRange(totals.ProteinG, Targets.ProteinG * 0.75m, Targets.ProteinG * 1.25m);
        Assert.InRange(totals.CarbsG, Targets.CarbsG * 0.75m, Targets.CarbsG * 1.25m);
        Assert.InRange(totals.Kcal, Targets.Kcal * 0.70m, Targets.Kcal * 1.30m);
    }

    [Fact]
    public void Restrictions_ExcludeMatchingFoods()
    {
        var diet = DietRuleEngine.Generate(Targets, Catalog(), ["frango", "leite"]);
        var names = diet.Meals.SelectMany(m => m.Items).Select(i => i.Name).ToList();
        Assert.DoesNotContain(names, n => n.Contains("frango", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("leite", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImpossibleRestrictions_ThrowInsteadOfEmptyPlan()
    {
        // Restringe todos os nomes com vogal "a" e "o" — inviabiliza o catálogo.
        var allNames = Catalog().Select(f => f.Name).ToList();
        Assert.ThrowsAny<InvalidOperationException>(() =>
            DietRuleEngine.Generate(Targets, Catalog(), allNames));
    }
}
