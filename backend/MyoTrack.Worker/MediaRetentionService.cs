using Microsoft.EntityFrameworkCore;
using MyoTrack.Domain.Entities;
using MyoTrack.Infrastructure;
using MyoTrack.Infrastructure.Storage;

namespace MyoTrack.Worker;

/// <summary>
/// Política de retenção de mídia (LGPD): vídeos são potencialmente biométricos e
/// expiram rápido; fotos de refeição duram mais. Os resultados das análises são
/// preservados — apenas os arquivos no storage são eliminados.
/// </summary>
public class MediaRetentionService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<MediaRetentionService> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro na varredura de retenção de mídia.");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var videoDays = configuration.GetValue("Retention:VideoDays", 30);
        var photoDays = configuration.GetValue("Retention:MealPhotoDays", 90);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IMediaStorage>();

        var videoCutoff = DateTimeOffset.UtcNow.AddDays(-videoDays);
        var videos = await db.ExerciseVideoAnalyses
            .Where(a => a.MediaExpiredAt == null && a.CreatedAt < videoCutoff)
            .Take(100)
            .ToListAsync(ct);
        foreach (var video in videos)
        {
            await DeleteQuietlyAsync(storage, video.MediaKey, ct);
            if (video.OverlayVideoKey is not null)
                await DeleteQuietlyAsync(storage, video.OverlayVideoKey, ct);
            video.MediaExpiredAt = DateTimeOffset.UtcNow;
        }

        var photoCutoff = DateTimeOffset.UtcNow.AddDays(-photoDays);
        var photos = await db.MealPhotoAnalyses
            .Where(a => a.MediaExpiredAt == null && a.CreatedAt < photoCutoff)
            .Take(100)
            .ToListAsync(ct);
        foreach (var photo in photos)
        {
            await DeleteQuietlyAsync(storage, photo.MediaKey, ct);
            photo.MediaExpiredAt = DateTimeOffset.UtcNow;
        }

        if (videos.Count > 0 || photos.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Retenção de mídia: {Videos} vídeo(s) e {Photos} foto(s) expirados.", videos.Count, photos.Count);
        }
    }

    private async Task DeleteQuietlyAsync(IMediaStorage storage, string key, CancellationToken ct)
    {
        try
        {
            await storage.DeleteAsync(key, ct);
        }
        catch (Exception ex)
        {
            // Objeto pode já não existir; a linha ainda será marcada como expirada.
            logger.LogWarning(ex, "Falha ao apagar mídia expirada {Key}.", key);
        }
    }
}
