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

    /// <summary>
    /// Status do Stripe que mantém o acesso Pro. past_due entra como período de
    /// graça: o Stripe ainda está tentando cobrar (smart retries); o corte
    /// definitivo vem com canceled/unpaid ou com customer.subscription.deleted.
    /// </summary>
    public static bool IsEntitledStatus(string? stripeStatus) =>
        stripeStatus is "active" or "trialing" or "past_due";

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
            PaymentPastDue = subscription?.StripeStatus == "past_due",
            HasStripeCustomer = subscription?.StripeCustomerId is not null,
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

    /// <summary>
    /// Portal do cliente Stripe: cancelar assinatura, trocar cartão, ver faturas.
    /// O Stripe é quem hospeda a tela — nada de dados de cartão passa pelo MyoTrack.
    /// </summary>
    [HttpPost("portal")]
    public async Task<IActionResult> Portal()
    {
        if (!Options.IsConfigured)
            return StatusCode(503, new { error = "Pagamentos ainda não estão disponíveis neste ambiente." });

        var subscription = await db.UserSubscriptions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.UserId == CurrentUserId);
        if (subscription?.StripeCustomerId is null)
            return BadRequest(new { error = "Você ainda não tem uma assinatura para gerenciar." });

        var client = new StripeClient(Options.SecretKey);
        var session = await new Stripe.BillingPortal.SessionService(client)
            .CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = subscription.StripeCustomerId,
                ReturnUrl = $"{Options.PublicBaseUrl}/assinatura",
            });

        return Ok(new { url = session.Url });
    }

    /// <summary>Webhook do Stripe — mantém o cache local de assinaturas em sincronia.</summary>
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

        // Idempotência: o Stripe reentrega eventos; o mesmo evt_ só é processado uma vez.
        db.StripeEventLogs.Add(new StripeEventLog { Id = stripeEvent.Id, Type = stripeEvent.Type });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            logger.LogInformation("Evento {EventId} já processado — ignorando reentrega.", stripeEvent.Id);
            return Ok();
        }

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                await HandleCheckoutCompletedAsync((Session)stripeEvent.Data.Object);
                break;
            case "customer.subscription.updated":
                await HandleSubscriptionUpdatedAsync((Subscription)stripeEvent.Data.Object);
                break;
            case "customer.subscription.deleted":
                await HandleSubscriptionDeletedAsync((Subscription)stripeEvent.Data.Object);
                break;
        }

        return Ok();
    }

    private async Task HandleCheckoutCompletedAsync(Session session)
    {
        if (!Guid.TryParse(session.ClientReferenceId, out var userId))
        {
            logger.LogWarning("checkout.session.completed sem ClientReferenceId válido.");
            return;
        }

        var subscription = await db.UserSubscriptions.SingleOrDefaultAsync(s => s.UserId == userId)
            ?? db.UserSubscriptions.Add(new UserSubscription { UserId = userId }).Entity;
        subscription.Plan = SubscriptionPlanType.Pro;
        subscription.IsActive = true;
        subscription.StripeCustomerId = session.CustomerId;
        subscription.StripeSubscriptionId = session.SubscriptionId;
        subscription.StripeStatus = "active";
        subscription.UpdatedAt = DateTimeOffset.UtcNow;

        // Busca a assinatura no Stripe para preencher o fim do período — best-effort.
        if (session.SubscriptionId is not null)
        {
            try
            {
                var client = new StripeClient(Options.SecretKey);
                var stripeSub = await new SubscriptionService(client).GetAsync(session.SubscriptionId);
                subscription.StripeStatus = stripeSub.Status;
                subscription.CurrentPeriodEnd = GetPeriodEnd(stripeSub);
            }
            catch (StripeException e)
            {
                logger.LogWarning(e, "Falha ao consultar a assinatura {SubscriptionId} no Stripe.",
                    session.SubscriptionId);
            }
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Assinatura Pro ativada para o usuário {UserId}.", userId);
    }

    private async Task HandleSubscriptionUpdatedAsync(Subscription stripeSubscription)
    {
        var subscription = await db.UserSubscriptions
            .SingleOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscription.Id);
        if (subscription is null)
        {
            logger.LogWarning("customer.subscription.updated para assinatura desconhecida {SubscriptionId}.",
                stripeSubscription.Id);
            return;
        }

        var entitled = IsEntitledStatus(stripeSubscription.Status);
        subscription.IsActive = entitled;
        subscription.Plan = entitled ? SubscriptionPlanType.Pro : SubscriptionPlanType.Free;
        subscription.StripeStatus = stripeSubscription.Status;
        subscription.CurrentPeriodEnd = GetPeriodEnd(stripeSubscription);
        subscription.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        logger.LogInformation("Assinatura do usuário {UserId} sincronizada: {Status}.",
            subscription.UserId, stripeSubscription.Status);
    }

    private async Task HandleSubscriptionDeletedAsync(Subscription stripeSubscription)
    {
        var subscription = await db.UserSubscriptions
            .SingleOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscription.Id);
        if (subscription is null)
            return;

        subscription.Plan = SubscriptionPlanType.Free;
        subscription.IsActive = false;
        subscription.StripeStatus = stripeSubscription.Status ?? "canceled";
        subscription.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        logger.LogInformation("Assinatura Pro encerrada para o usuário {UserId}.", subscription.UserId);
    }

    /// <summary>
    /// Na API atual do Stripe o fim do período fica nos itens da assinatura
    /// (todos os itens compartilham o mesmo ciclo; usa o maior por segurança).
    /// </summary>
    private static DateTimeOffset? GetPeriodEnd(Subscription subscription) =>
        subscription.Items?.Data is { Count: > 0 } items
            ? new DateTimeOffset(items.Max(i => i.CurrentPeriodEnd), TimeSpan.Zero)
            : null;
}
