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
    public DateTimeOffset? CurrentPeriodEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
