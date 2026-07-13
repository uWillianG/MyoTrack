namespace MyoTrack.Domain.Entities;

/// <summary>Trilha de consentimento exigida pela LGPD para dados sensíveis de saúde.</summary>
public class ConsentRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ConsentType Type { get; set; }
    public string TermsVersion { get; set; } = null!;
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
}
