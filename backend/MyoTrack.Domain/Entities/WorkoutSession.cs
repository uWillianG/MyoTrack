namespace MyoTrack.Domain.Entities;

/// <summary>Execução real de um treino; a progressão de carga deriva dos SetLogs.</summary>
public class WorkoutSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? WorkoutDayId { get; set; }
    public WorkoutDay? WorkoutDay { get; set; }
    public DateOnly Date { get; set; }
    public string? Notes { get; set; }

    public List<SetLog> Sets { get; set; } = [];
}

public class SetLog
{
    public Guid Id { get; set; }
    public Guid WorkoutSessionId { get; set; }
    public WorkoutSession WorkoutSession { get; set; } = null!;
    public int ExerciseId { get; set; }
    public Exercise Exercise { get; set; } = null!;
    public int SetNumber { get; set; }
    public int Reps { get; set; }
    public decimal LoadKg { get; set; }
    /// <summary>Rate of Perceived Exertion (1–10), opcional.</summary>
    public int? Rpe { get; set; }
}
