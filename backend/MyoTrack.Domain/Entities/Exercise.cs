namespace MyoTrack.Domain.Entities;

/// <summary>Catálogo global de exercícios.</summary>
public class Exercise
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public MuscleGroup PrimaryMuscleGroup { get; set; }
    public List<MuscleGroup> SecondaryMuscleGroups { get; set; } = [];
    public Equipment Equipment { get; set; }
    public string? Instructions { get; set; }
    /// <summary>Tags de contraindicação cruzadas com UserProfile.InjuryTags (ex.: "knee", "lower-back", "shoulder").</summary>
    public List<string> ContraindicationTags { get; set; } = [];
    public string? MediaUrl { get; set; }
    public bool IsCompound { get; set; }
}
