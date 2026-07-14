using Microsoft.EntityFrameworkCore;
using MyoTrack.Domain;
using MyoTrack.Infrastructure;

namespace MyoTrack.Api.Services;

public record Entitlements(SubscriptionPlanType Plan, int MaxMealAnalysesPerDay, int MaxVideoAnalysesPerDay);

/// <summary>
/// Resolve o plano do usuário (Free/Pro) e os limites de uso de IA correspondentes.
/// Limites são configuráveis por plano em "Limits:Free:*" / "Limits:Pro:*".
/// </summary>
public class EntitlementService(AppDbContext db, IConfiguration configuration)
{
    public async Task<Entitlements> GetAsync(Guid userId)
    {
        var isPro = await db.UserSubscriptions.AsNoTracking().AnyAsync(s =>
            s.UserId == userId && s.IsActive && s.Plan == SubscriptionPlanType.Pro);

        return isPro
            ? new Entitlements(SubscriptionPlanType.Pro,
                configuration.GetValue("Limits:Pro:MaxMealAnalysesPerDay", 50),
                configuration.GetValue("Limits:Pro:MaxVideoAnalysesPerDay", 20))
            : new Entitlements(SubscriptionPlanType.Free,
                configuration.GetValue("Limits:Free:MaxMealAnalysesPerDay", 10),
                configuration.GetValue("Limits:Free:MaxVideoAnalysesPerDay", 5));
    }
}
