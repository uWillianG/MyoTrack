namespace MyoTrack.Domain.Entities;

public class DietPlan
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = null!;
    public CalorieGoal CalorieGoal { get; set; }
    public PlanStatus Status { get; set; } = PlanStatus.Draft;
    public int Version { get; set; } = 1;

    /// <summary>Metas calculadas deterministicamente (TDEE, macros) — nunca pelo LLM.</summary>
    public decimal TargetKcal { get; set; }
    public decimal TargetProteinG { get; set; }
    public decimal TargetCarbsG { get; set; }
    public decimal TargetFatG { get; set; }

    public string? GenerationInputJson { get; set; }
    public string? RawLlmOutputJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Meal> Meals { get; set; } = [];
}

public class Meal
{
    public Guid Id { get; set; }
    public Guid DietPlanId { get; set; }
    public DietPlan DietPlan { get; set; } = null!;
    public int Order { get; set; }
    /// <summary>Ex.: "Café da manhã", "Almoço".</summary>
    public string Name { get; set; } = null!;

    public List<MealItem> Items { get; set; } = [];
}

public class MealItem
{
    public Guid Id { get; set; }
    public Guid MealId { get; set; }
    public Meal Meal { get; set; } = null!;
    public int FoodItemId { get; set; }
    public FoodItem FoodItem { get; set; } = null!;
    public decimal QuantityG { get; set; }
}
