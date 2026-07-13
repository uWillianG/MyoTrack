namespace MyoTrack.Domain.Entities;

/// <summary>Trilha de consumo de IA por usuário — base para limites e controle de custo.</summary>
public class AiUsageLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AnalysisJobType Operation { get; set; }
    public string Model { get; set; } = null!;
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
