namespace MyoTrack.Domain.Entities;

public class WorkoutPlan
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = null!;
    public FitnessGoal Goal { get; set; }
    /// <summary>Ex.: "ABC", "ABCD", "PPL", "FullBody".</summary>
    public string Split { get; set; } = null!;
    public PlanStatus Status { get; set; } = PlanStatus.Draft;
    public int Version { get; set; } = 1;

    /// <summary>Snapshot JSON do perfil/inputs usados na geração (auditoria e cache).</summary>
    public string? GenerationInputJson { get; set; }
    /// <summary>Resposta bruta do LLM (auditoria).</summary>
    public string? RawLlmOutputJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<WorkoutDay> Days { get; set; } = [];
}

public class WorkoutDay
{
    public Guid Id { get; set; }
    public Guid WorkoutPlanId { get; set; }
    public WorkoutPlan WorkoutPlan { get; set; } = null!;
    public int Order { get; set; }
    /// <summary>Ex.: "A — Peito/Tríceps".</summary>
    public string Label { get; set; } = null!;

    public List<WorkoutExercise> Exercises { get; set; } = [];
}

public class WorkoutExercise
{
    public Guid Id { get; set; }
    public Guid WorkoutDayId { get; set; }
    public WorkoutDay WorkoutDay { get; set; } = null!;
    public int ExerciseId { get; set; }
    public Exercise Exercise { get; set; } = null!;
    public int Order { get; set; }
    public int Sets { get; set; }
    public int RepsMin { get; set; }
    public int RepsMax { get; set; }
    public decimal? SuggestedLoadKg { get; set; }
    public int RestSeconds { get; set; }
    public string? Notes { get; set; }
}
