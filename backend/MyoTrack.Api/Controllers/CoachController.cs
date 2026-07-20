using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Api.Services;
using MyoTrack.Domain;
using MyoTrack.Domain.Entities;
using MyoTrack.Infrastructure;

namespace MyoTrack.Api.Controllers;

public record CoachMessageRequest(string Content);

/// <summary>
/// Chat com o coach IA. A resposta roda como job na fila (a chave de LLM vive
/// só no Worker); o cliente acompanha via SSE/polling como as demais análises.
/// </summary>
[Route("api/coach")]
public class CoachController(AppDbContext db, EntitlementService entitlements) : ApiControllerBase
{
    private const int MaxContentLength = 2000;

    [HttpGet("messages")]
    public async Task<IActionResult> List()
    {
        var messages = await db.CoachMessages.AsNoTracking()
            .Where(m => m.UserId == CurrentUserId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(50)
            .Select(m => new { m.Id, m.FromUser, m.Content, m.CreatedAt })
            .ToListAsync();
        messages.Reverse();
        return Ok(messages);
    }

    [HttpPost("messages")]
    public async Task<IActionResult> Send(CoachMessageRequest request)
    {
        var content = request.Content?.Trim();
        if (string.IsNullOrEmpty(content))
            return BadRequest(new { error = "Escreva uma mensagem para o coach." });
        if (content.Length > MaxContentLength)
            return BadRequest(new { error = $"Mensagem muito longa (máximo {MaxContentLength} caracteres)." });

        var pending = await db.AnalysisJobs.AnyAsync(j =>
            j.UserId == CurrentUserId &&
            j.Type == AnalysisJobType.CoachChat &&
            (j.Status == JobStatus.Pending || j.Status == JobStatus.Processing));
        if (pending)
            return Conflict(new { error = "Aguarde a resposta anterior do coach." });

        var entitlement = await entitlements.GetAsync(CurrentUserId);
        // Meia-noite UTC explícita — DateTime local implícito é rejeitado pelo Npgsql em timestamptz.
        var since = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var usedToday = await db.CoachMessages.CountAsync(m =>
            m.UserId == CurrentUserId && m.FromUser && m.CreatedAt >= since);
        if (usedToday >= entitlement.MaxCoachMessagesPerDay)
            return StatusCode(429, new
            {
                error = entitlement.Plan == SubscriptionPlanType.Free
                    ? $"Limite diário de {entitlement.MaxCoachMessagesPerDay} mensagens atingido. Assine o Pro para conversar mais."
                    : $"Limite diário de {entitlement.MaxCoachMessagesPerDay} mensagens ao coach atingido.",
            });

        db.CoachMessages.Add(new CoachMessage
        {
            UserId = CurrentUserId,
            FromUser = true,
            Content = content,
        });
        var job = new AnalysisJob { UserId = CurrentUserId, Type = AnalysisJobType.CoachChat };
        db.AnalysisJobs.Add(job);
        await db.SaveChangesAsync();
        return Accepted(new { jobId = job.Id });
    }
}
