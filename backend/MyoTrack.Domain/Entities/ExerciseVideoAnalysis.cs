namespace MyoTrack.Domain.Entities;

/// <summary>
/// Resultado da análise de execução de exercício por vídeo (MediaPipe Pose no serviço vision).
/// Erros detectados e métricas ficam em JSONB para evoluir as heurísticas sem migração.
/// </summary>
public class ExerciseVideoAnalysis
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid AnalysisJobId { get; set; }
    public string MediaKey { get; set; } = null!;

    /// <summary>Vídeo com o esqueleto desenhado, gerado pelo serviço vision.</summary>
    public string? OverlayVideoKey { get; set; }

    /// <summary>Slug do exercício analisado (squat, deadlift, overhead_press).</summary>
    public string AnalyzedExercise { get; set; } = null!;

    /// <summary>0–100; null quando a pose não pôde ser avaliada com confiança.</summary>
    public int? Score { get; set; }

    public int RepCount { get; set; }

    /// <summary>{issues: [{code, message, timestampsSec[]}], metrics: {...}, notEvaluableReason?}.</summary>
    public string ResultJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Quando a mídia foi apagada do storage pela política de retenção (LGPD).</summary>
    public DateTimeOffset? MediaExpiredAt { get; set; }
}
