namespace MyoTrack.Domain.Entities;

/// <summary>
/// Fila de jobs de IA persistida no Postgres (consumida com FOR UPDATE SKIP LOCKED).
/// Cobre gerações via LLM e análises de mídia.
/// </summary>
public class AnalysisJob
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AnalysisJobType Type { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Pending;
    /// <summary>Chave do objeto no MinIO (vídeo/foto), quando aplicável.</summary>
    public string? MediaKey { get; set; }
    /// <summary>Payload de entrada específico do tipo de job (JSON).</summary>
    public string? InputJson { get; set; }
    /// <summary>Resultado do processamento (JSON).</summary>
    public string? ResultJson { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
