using Microsoft.EntityFrameworkCore;
using MyoTrack.Domain;
using MyoTrack.Domain.Entities;
using MyoTrack.Infrastructure;

namespace MyoTrack.Worker;

/// <summary>
/// Enfileira o relatório semanal (segunda a domingo, UTC) de cada usuário que
/// teve atividade na última semana completa e ainda não tem o relatório dela.
/// Idempotente: roda de hora em hora, mas cada usuário/semana gera no máximo
/// um job — é a garantia de "1 chamada de LLM por usuário por semana".
/// </summary>
public class WeeklyReportSchedulerService(
    IServiceScopeFactory scopeFactory,
    ILogger<WeeklyReportSchedulerService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnqueuePendingReportsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao agendar relatórios semanais.");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Segunda-feira (UTC) da semana que contém a data.</summary>
    internal static DateOnly WeekStartOf(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private async Task EnqueuePendingReportsAsync(CancellationToken ct)
    {
        var lastWeekStart = WeekStartOf(DateOnly.FromDateTime(DateTime.UtcNow)).AddDays(-7);
        var lastWeekEnd = lastWeekStart.AddDays(7);
        var startUtc = new DateTimeOffset(lastWeekStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var endUtc = new DateTimeOffset(lastWeekEnd.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Usuários com qualquer atividade na semana: treino, refeição no diário ou medição.
        var active = await db.WorkoutSessions
            .Where(s => s.Date >= lastWeekStart && s.Date < lastWeekEnd)
            .Select(s => s.UserId)
            .Union(db.MealPhotoAnalyses
                .Where(a => !a.ExcludedFromDiary && a.CreatedAt >= startUtc && a.CreatedAt < endUtc)
                .Select(a => a.UserId))
            .Union(db.BodyMeasurements
                .Where(m => m.Date >= lastWeekStart && m.Date < lastWeekEnd)
                .Select(m => m.UserId))
            .Distinct()
            .ToListAsync(ct);
        if (active.Count == 0)
            return;

        var withReport = await db.WeeklyReports
            .Where(r => r.WeekStart == lastWeekStart && active.Contains(r.UserId))
            .Select(r => r.UserId)
            .ToListAsync(ct);
        var withJob = await db.AnalysisJobs
            .Where(j => j.Type == AnalysisJobType.WeeklyReport &&
                        (j.Status == JobStatus.Pending || j.Status == JobStatus.Processing) &&
                        active.Contains(j.UserId))
            .Select(j => j.UserId)
            .ToListAsync(ct);

        var pending = active.Except(withReport).Except(withJob).ToList();
        foreach (var userId in pending)
        {
            db.AnalysisJobs.Add(new AnalysisJob
            {
                UserId = userId,
                Type = AnalysisJobType.WeeklyReport,
                InputJson = $$"""{"weekStart":"{{lastWeekStart:yyyy-MM-dd}}"}""",
            });
        }

        if (pending.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Relatórios semanais: {Count} job(s) enfileirado(s) para a semana de {WeekStart}.",
                pending.Count, lastWeekStart);
        }
    }
}
