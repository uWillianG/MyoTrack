using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyoTrack.Domain;
using MyoTrack.Domain.Entities;
using MyoTrack.Infrastructure;
using MyoTrack.Infrastructure.Storage;

namespace MyoTrack.Api.Controllers;

public record PresignVideoRequest(string ContentType);
public record CreateVideoAnalysisRequest(string MediaKey, string Exercise);

[Route("api/video-analyses")]
public class VideoAnalysesController(
    AppDbContext db,
    IMediaStorage storage,
    MyoTrack.Api.Services.EntitlementService entitlements) : ApiControllerBase
{
    private const long MaxVideoBytes = 100 * 1024 * 1024;
    private static readonly TimeSpan UploadUrlExpiry = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan PlaybackUrlExpiry = TimeSpan.FromHours(1);

    private static readonly Dictionary<string, string> AllowedContentTypes = new()
    {
        ["video/mp4"] = ".mp4",
        ["video/quicktime"] = ".mov",
        ["video/webm"] = ".webm",
    };

    /// <summary>Exercícios com heurísticas implementadas no serviço vision (SPECS em vision/app/heuristics.py).</summary>
    private static readonly string[] SupportedExercises =
    [
        "squat", "lunge", "deadlift", "romanian_deadlift", "hip_thrust",
        "bench_press", "push_up", "overhead_press", "barbell_row",
        "biceps_curl", "pull_up", "lateral_raise",
    ];

    /// <summary>Passo 1: URL pré-assinada para o browser subir o vídeo direto no MinIO.</summary>
    [HttpPost("presign")]
    public async Task<IActionResult> Presign(PresignVideoRequest request)
    {
        if (!AllowedContentTypes.TryGetValue(request.ContentType, out var extension))
            return BadRequest(new { error = "Formato não suportado. Use MP4, MOV ou WebM." });

        var key = $"videos/{CurrentUserId}/{Guid.NewGuid():N}{extension}";
        var url = await storage.GetPresignedUploadUrlAsync(key, request.ContentType, UploadUrlExpiry);
        return Ok(new { mediaKey = key, uploadUrl = url, maxBytes = MaxVideoBytes });
    }

    /// <summary>Passo 2: valida o objeto subido e enfileira a análise.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateVideoAnalysisRequest request)
    {
        if (!SupportedExercises.Contains(request.Exercise))
            return BadRequest(new { error = "Exercício não suportado para análise de vídeo." });
        // Só aceita chaves criadas pelo presign do próprio usuário.
        if (!request.MediaKey.StartsWith($"videos/{CurrentUserId}/", StringComparison.Ordinal))
            return BadRequest(new { error = "Chave de mídia inválida." });

        var entitlement = await entitlements.GetAsync(CurrentUserId);
        // Meia-noite UTC como DateTimeOffset explícito — a conversão implícita de
        // DateTime usaria o fuso local, que o Npgsql rejeita em timestamptz.
        var since = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var usedToday = await db.AnalysisJobs.CountAsync(j =>
            j.UserId == CurrentUserId &&
            j.Type == AnalysisJobType.ExerciseVideo &&
            j.CreatedAt >= since);
        if (usedToday >= entitlement.MaxVideoAnalysesPerDay)
            return StatusCode(429, new
            {
                error = entitlement.Plan == SubscriptionPlanType.Free
                    ? $"Limite diário de {entitlement.MaxVideoAnalysesPerDay} análises de vídeo atingido. Assine o Pro para ampliar."
                    : $"Limite diário de {entitlement.MaxVideoAnalysesPerDay} análises de vídeo atingido.",
            });

        var info = await storage.GetObjectInfoAsync(request.MediaKey);
        if (info is null)
            return BadRequest(new { error = "Vídeo não encontrado no storage. Refaça o upload." });
        if (info.SizeBytes is 0 or > MaxVideoBytes)
        {
            await storage.DeleteAsync(request.MediaKey);
            return BadRequest(new { error = "Envie um vídeo de até 100 MB." });
        }

        var job = new AnalysisJob
        {
            UserId = CurrentUserId,
            Type = AnalysisJobType.ExerciseVideo,
            MediaKey = request.MediaKey,
            InputJson = JsonSerializer.Serialize(new { exercise = request.Exercise }),
        };
        db.AnalysisJobs.Add(job);
        await db.SaveChangesAsync();
        return Accepted(new { jobId = job.Id });
    }

    [HttpGet("supported-exercises")]
    public IActionResult Supported() => Ok(SupportedExercises);

    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await db.ExerciseVideoAnalyses.AsNoTracking()
            .Where(a => a.UserId == CurrentUserId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(50)
            .Select(a => new { a.Id, a.CreatedAt, a.AnalyzedExercise, a.Score, a.RepCount })
            .ToListAsync());

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var analysis = await db.ExerciseVideoAnalyses.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == id && a.UserId == CurrentUserId);
        if (analysis is null)
            return NotFound();

        // URL do overlay (ou do original, como fallback) para reprodução no player.
        // Mídia expirada pela política de retenção não tem mais arquivo no storage.
        string? playbackUrl = null;
        if (analysis.MediaExpiredAt is null)
        {
            var playbackKey = analysis.OverlayVideoKey ?? analysis.MediaKey;
            playbackUrl = await storage.GetPresignedDownloadUrlAsync(playbackKey, PlaybackUrlExpiry);
        }

        return Ok(new
        {
            analysis.Id,
            analysis.CreatedAt,
            analysis.AnalyzedExercise,
            analysis.Score,
            analysis.RepCount,
            PlaybackUrl = playbackUrl,
            MediaExpired = analysis.MediaExpiredAt is not null,
            HasOverlay = analysis.OverlayVideoKey is not null,
            Result = JsonSerializer.Deserialize<JsonElement>(analysis.ResultJson),
        });
    }
}
