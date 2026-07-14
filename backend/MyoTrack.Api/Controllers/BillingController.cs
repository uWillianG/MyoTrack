using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyoTrack.Api.Services;
using MyoTrack.Domain;
using MyoTrack.Domain.Entities;
using MyoTrack.Infrastructure;
using Stripe;
using Stripe.Checkout;

namespace MyoTrack.Api.Controllers;

public class BillingOptions
{
    public const string SectionName = "Billing";

    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string ProPriceId { get; set; } = string.Empty;

    /// <summary>Base pública do frontend para redirecionos do Checkout (sucesso/cancelamento).</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5173";

    public bool IsConfigured => !string.IsNullOrEmpty(SecretKey) && !string.IsNullOrEmpty(ProPriceId);
}

[Route("api/billing")]
public class BillingController(
    AppDbContext db,
    EntitlementService entitlements,
    IOptions<BillingOptions> billingOptions,
    ILogger<BillingController> logger) : ApiControllerBase
{
    private BillingOptions Options => billingOptions.Value;

    /// <summary>Plano atual do usuário + limites de uso, para a página de assinatura.</summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var entitlement = await entitlements.GetAsync(CurrentUserId);
        var subscription = await db.UserSubscriptions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.UserId == CurrentUserId);

        return Ok(new
        {
            Plan = entitlement.Plan.ToString(),
            entitlement.MaxMealAnalysesPerDay,
            entitlement.MaxVideoAnalysesPerDay,
            subscription?.CurrentPeriodEnd,
            BillingConfigured = Options.IsConfigured,
        });
    }

    /// <summary>Cria uma sessão do Stripe Checkout para assinar o plano Pro.</summary>
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout()
    {
        if (!Options.IsConfigured)
            return StatusCode(503, new { error = "Pagamentos ainda não estão disponíveis neste ambiente." });

        var entitlement = await entitlements.GetAsync(CurrentUserId);
        if (entitlement.Plan == SubscriptionPlanType.Pro)
            return Conflict(new { error = "Você já é assinante Pro." });

        var email = await db.Users.Where(u => u.Id == CurrentUserId)
            .Select(u => u.Email).SingleAsync();

        var client = new StripeClient(Options.SecretKey);
        var session = await new SessionService(client).CreateAsync(new SessionCreateOptions
        {
            Mode = "subscription",
            LineItems = [new SessionLineItemOptions { Price = Options.ProPriceId, Quantity = 1 }],
            // O webhook usa o ClientReferenceId para saber qual usuário ativou o Pro.
            ClientReferenceId = CurrentUserId.ToString(),
            CustomerEmail = email,
            SuccessUrl = $"{Options.PublicBaseUrl}/assinatura?status=sucesso",
            CancelUrl = $"{Options.PublicBaseUrl}/assinatura?status=cancelado",
        });

        return Ok(new { url = session.Url });
    }

    /// <summary>Webhook do Stripe — ativa/desativa o Pro conforme eventos de assinatura.</summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook()
    {
        if (string.IsNullOrEmpty(Options.WebhookSecret))
            return NotFound();

        Event stripeEvent;
        try
        {
            var payload = await new StreamReader(Request.Body).ReadToEndAsync();
            stripeEvent = EventUtility.ConstructEvent(
                payload, Request.Headers["Stripe-Signature"], Options.WebhookSecret);
        }
        catch (StripeException e)
        {
            logger.LogWarning(e, "Webhook do Stripe com assinatura inválida.");
            return BadRequest();
        }

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
            {
                var session = (Session)stripeEvent.Data.Object;
                if (!Guid.TryParse(session.ClientReferenceId, out var userId))
                {
                    logger.LogWarning("checkout.session.completed sem ClientReferenceId válido.");
                    break;
                }

                var subscription = await db.UserSubscriptions.SingleOrDefaultAsync(s => s.UserId == userId)
                    ?? db.UserSubscriptions.Add(new UserSubscription { UserId = userId }).Entity;
                subscription.Plan = SubscriptionPlanType.Pro;
                subscription.IsActive = true;
                subscription.StripeCustomerId = session.CustomerId;
                subscription.StripeSubscriptionId = session.SubscriptionId;
                subscription.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
                logger.LogInformation("Assinatura Pro ativada para o usuário {UserId}.", userId);
                break;
            }
            case "customer.subscription.deleted":
            {
                var stripeSubscription = (Subscription)stripeEvent.Data.Object;
                var subscription = await db.UserSubscriptions
                    .SingleOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscription.Id);
                if (subscription is not null)
                {
                    subscription.Plan = SubscriptionPlanType.Free;
                    subscription.IsActive = false;
                    subscription.UpdatedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync();
                    logger.LogInformation("Assinatura Pro encerrada para o usuário {UserId}.", subscription.UserId);
                }
                break;
            }
        }

        return Ok();
    }
}
