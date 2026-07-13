namespace MyoTrack.Domain.Entities;

public class UserProfile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public DateOnly? BirthDate { get; set; }
    public string? Sex { get; set; } // "M" | "F"
    public decimal? HeightCm { get; set; }
    public Biotype? Biotype { get; set; }
    public ExperienceLevel ExperienceLevel { get; set; } = ExperienceLevel.Beginner;
    public FitnessGoal Goal { get; set; } = FitnessGoal.Hypertrophy;
    public int TrainingDaysPerWeek { get; set; } = 3;

    /// <summary>Grupos musculares priorizados pelo usuário.</summary>
    public List<MuscleGroup> PriorityMuscleGroups { get; set; } = [];

    /// <summary>Lesões/limitações em texto livre + tags estruturadas.</summary>
    public string? InjuryNotes { get; set; }
    public List<string> InjuryTags { get; set; } = [];

    public List<Equipment> AvailableEquipment { get; set; } = [];

    public List<string> DietaryRestrictions { get; set; } = [];
    public List<string> FoodPreferences { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
