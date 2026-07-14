namespace MyoTrack.Domain.Entities;

/// <summary>
/// Assinatura do usuário. Sem registro (ou inativa) = plano Free.
/// O Stripe é a fonte da verdade; esta tabela é o cache local mantido pelo webhook.
/// </summary>
public class UserSubscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public SubscriptionPlanType Plan { get; set; } = SubscriptionPlanType.Free;
    public bool IsActive { get; set; }
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    /// <summary>Status bruto do Stripe (active, trialing, past_due, canceled…) para diagnóstico e UI.</summary>
    public string? StripeStatus { get; set; }
    public DateTimeOffset? CurrentPeriodEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Eventos do Stripe já processados — o webhook pode reentregar o mesmo evento,
/// e o registro garante idempotência (além de servir como trilha de auditoria).
/// </summary>
public class StripeEventLog
{
    /// <summary>Id do evento no Stripe (evt_...).</summary>
    public string Id { get; set; } = null!;
    public string Type { get; set; } = null!;
    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
}
