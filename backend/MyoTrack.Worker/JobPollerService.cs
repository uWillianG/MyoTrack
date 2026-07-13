using Microsoft.EntityFrameworkCore;
using MyoTrack.Domain;
using MyoTrack.Domain.Entities;
using MyoTrack.Infrastructure;

namespace MyoTrack.Worker;

/// <summary>
/// Consome a fila de jobs de IA persistida no Postgres usando FOR UPDATE SKIP LOCKED,
/// o que permite múltiplas instâncias do worker sem processamento duplicado.
/// Os handlers por tipo de job entram nas Fases 1–3.
/// </summary>
public class JobPollerService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<JobPollerService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("JobPollerService iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessNextJobAsync(stoppingToken);
                if (!processed)
                    await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro no loop de polling de jobs.");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessNextJobAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var job = await db.AnalysisJobs
            .FromSqlRaw("""
                SELECT * FROM "AnalysisJobs"
                WHERE "Status" = {0}
                ORDER BY "CreatedAt"
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """, (int)JobStatus.Pending)
            .FirstOrDefaultAsync(ct);

        if (job is null)
            return false;

        job.Status = JobStatus.Processing;
        job.StartedAt = DateTimeOffset.UtcNow;
        job.Attempts++;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        try
        {
            await HandleAsync(job, ct);
            job.Status = JobStatus.Completed;
            job.CompletedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao processar job {JobId} ({JobType}).", job.Id, job.Type);
            job.LastError = ex.Message;
            job.Status = job.Attempts >= MaxAttempts ? JobStatus.Failed : JobStatus.Pending;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    private Task HandleAsync(AnalysisJob job, CancellationToken ct) => job.Type switch
    {
        // Handlers reais entram nas próximas fases:
        // WorkoutGeneration / DietGeneration → Fase 1 (LLM)
        // MealPhoto → Fase 2 (LLM multimodal)
        // ExerciseVideo → Fase 3 (serviço Python/MediaPipe)
        _ => throw new NotSupportedException($"Nenhum handler registrado para o tipo de job '{job.Type}'."),
    };
}
