using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Domain;
using MyoTrack.Infrastructure;

namespace MyoTrack.Api.Controllers;

[Route("api/jobs")]
public class JobsController(AppDbContext db) : ApiControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var job = await GetJobAsync(id, HttpContext.RequestAborted);
        return job is null ? NotFound() : Ok(job);
    }

    /// <summary>
    /// Status do job via Server-Sent Events: um evento por mudança de estado,
    /// encerrando quando o job termina. Auth via query `access_token` (EventSource
    /// não envia headers).
    /// </summary>
    [HttpGet("{id:guid}/stream")]
    public async Task Stream(Guid id, CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        object? previous = null;
        // Vídeos podem levar vários minutos; o cliente reconecta se o stream expirar.
        var deadline = DateTimeOffset.UtcNow.AddMinutes(15);
        while (!ct.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
        {
            var job = await GetJobAsync(id, ct);
            if (job is null)
            {
                await WriteEventAsync(new { error = "not_found" }, ct);
                return;
            }

            if (previous is null || !JsonSerializer.Serialize(job, JsonOptions).Equals(
                    JsonSerializer.Serialize(previous, JsonOptions), StringComparison.Ordinal))
            {
                await WriteEventAsync(job, ct);
                previous = job;
            }

            if (job.Status is nameof(JobStatus.Completed) or nameof(JobStatus.Failed))
                return;

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    private async Task WriteEventAsync(object payload, CancellationToken ct)
    {
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(payload, JsonOptions)}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    private async Task<JobDto?> GetJobAsync(Guid id, CancellationToken ct)
    {
        var job = await db.AnalysisJobs.AsNoTracking()
            .SingleOrDefaultAsync(j => j.Id == id && j.UserId == CurrentUserId, ct);
        return job is null
            ? null
            : new JobDto(job.Id, job.Type.ToString(), job.Status.ToString(),
                job.ResultJson, job.LastError, job.CreatedAt, job.CompletedAt);
    }

    private sealed record JobDto(
        Guid Id, string Type, string Status, string? ResultJson, string? LastError,
        DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);
}
