using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Domain;
using MyoTrack.Domain.Entities;
using MyoTrack.Infrastructure;

namespace MyoTrack.Api.Controllers;

/// <summary>
/// Relatórios semanais. A geração automática é do Worker (scheduler); o POST
/// manual existe para o usuário não esperar até uma hora pela primeira geração.
/// </summary>
[Route("api/reports")]
public class ReportsController(AppDbContext db) : ApiControllerBase
{
    /// <summary>Segunda-feira (UTC) da semana que contém a data.</summary>
    private static DateOnly WeekStartOf(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    [HttpGet("weekly")]
    public async Task<IActionResult> List()
    {
        var reports = await db.WeeklyReports.AsNoTracking()
            .Where(r => r.UserId == CurrentUserId)
            .OrderByDescending(r => r.WeekStart)
            .Take(12)
            .ToListAsync();

        return Ok(reports.Select(r => new
        {
            r.Id,
            r.WeekStart,
            r.CreatedAt,
            Metrics = JsonSerializer.Deserialize<JsonElement>(r.MetricsJson),
            Narrative = r.NarrativeJson is null
                ? (JsonElement?)null
                : JsonSerializer.Deserialize<JsonElement>(r.NarrativeJson),
        }));
    }

    /// <summary>Gera (uma única vez) o relatório da última semana completa.</summary>
    [HttpPost("weekly/generate")]
    public async Task<IActionResult> Generate()
    {
        var lastWeekStart = WeekStartOf(DateOnly.FromDateTime(DateTime.UtcNow)).AddDays(-7);

        var exists = await db.WeeklyReports.AnyAsync(r =>
            r.UserId == CurrentUserId && r.WeekStart == lastWeekStart);
        if (exists)
            return Conflict(new { error = "O relatório da última semana já foi gerado." });

        var pending = await db.AnalysisJobs.AnyAsync(j =>
            j.UserId == CurrentUserId &&
            j.Type == AnalysisJobType.WeeklyReport &&
            (j.Status == JobStatus.Pending || j.Status == JobStatus.Processing));
        if (pending)
            return Conflict(new { error = "Já existe um relatório em geração." });

        var job = new AnalysisJob
        {
            UserId = CurrentUserId,
            Type = AnalysisJobType.WeeklyReport,
            InputJson = $$"""{"weekStart":"{{lastWeekStart:yyyy-MM-dd}}"}""",
        };
        db.AnalysisJobs.Add(job);
        await db.SaveChangesAsync();
        return Accepted(new { jobId = job.Id });
    }
}
