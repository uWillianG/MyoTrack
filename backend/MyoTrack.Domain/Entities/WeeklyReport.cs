namespace MyoTrack.Domain.Entities;

/// <summary>
/// Relatório semanal do usuário: métricas calculadas em código (treinos, volume,
/// recordes, aderência à dieta, peso) + narrativa curta gerada pelo LLM.
/// Um por usuário por semana (semana ISO em UTC, começando na segunda-feira).
/// </summary>
public class WeeklyReport
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Segunda-feira (UTC) da semana coberta.</summary>
    public DateOnly WeekStart { get; set; }

    /// <summary>Métricas determinísticas da semana (JSONB) — a fonte dos números.</summary>
    public string MetricsJson { get; set; } = "{}";

    /// <summary>
    /// Narrativa do LLM: { summary, highlights[], recommendations[] } (JSONB).
    /// Null quando a IA está indisponível — o relatório vale pelos números.
    /// </summary>
    public string? NarrativeJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
