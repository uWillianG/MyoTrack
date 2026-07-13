namespace MyoTrack.Domain.Entities;

/// <summary>Catálogo nutricional (valores por 100 g). Fonte primária: TACO/TBCA.</summary>
public class FoodItem
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public decimal KcalPer100g { get; set; }
    public decimal ProteinPer100g { get; set; }
    public decimal CarbsPer100g { get; set; }
    public decimal FatPer100g { get; set; }
    public decimal? FiberPer100g { get; set; }
    /// <summary>"TACO", "TBCA", "Custom".</summary>
    public string Source { get; set; } = "TACO";
}
