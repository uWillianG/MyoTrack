namespace MyoTrack.Infrastructure.Identity;

/// <summary>
/// Código de uso único que o callback do OAuth entrega à SPA. O par de tokens
/// nunca viaja na URL de redirecionamento (ficaria no histórico do navegador e
/// nos logs de proxy) — a SPA troca este código por eles num POST.
/// </summary>
public class LoginCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    /// <summary>Hash SHA-256 do código — o valor bruto nunca é persistido.</summary>
    public string CodeHash { get; set; } = null!;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UsedAt { get; set; }

    public bool IsUsable => UsedAt is null && DateTimeOffset.UtcNow < ExpiresAt;
}
