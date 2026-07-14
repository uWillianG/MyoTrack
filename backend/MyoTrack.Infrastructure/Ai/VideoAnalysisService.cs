using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyoTrack.Domain.Entities;

namespace MyoTrack.Infrastructure.Ai;

public class VisionOptions
{
    public const string SectionName = "Vision";
    public string BaseUrl { get; set; } = "http://localhost:8000";
    /// <summary>Análise de vídeo é lenta (pose por frame + encode do overlay).</summary>
    public int TimeoutSeconds { get; set; } = 600;
}

public record VisionIssue(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("timestamps_sec")] List<double> TimestampsSec);

public record VisionAnalyzeResponse(
    [property: JsonPropertyName("score")] int? Score,
    [property: JsonPropertyName("rep_count")] int RepCount,
    [property: JsonPropertyName("issues")] List<VisionIssue> Issues,
    [property: JsonPropertyName("metrics")] JsonElement Metrics,
    [property: JsonPropertyName("not_evaluable_reason")] string? NotEvaluableReason,
    [property: JsonPropertyName("overlay_key")] string? OverlayKey);

/// <summary>
/// Handler do job ExerciseVideo: delega o processamento ao serviço vision
/// (Python/MediaPipe) via HTTP e persiste o resultado.
/// </summary>
public class VideoAnalysisService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    IOptions<VisionOptions> options,
    ILogger<VideoAnalysisService> logger)
{
    public const string HttpClientName = "vision";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> AnalyzeAsync(AnalysisJob job, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(job.MediaKey))
            throw new InvalidOperationException("Job de vídeo sem mídia associada.");

        var input = JsonSerializer.Deserialize<JsonElement>(job.InputJson ?? "{}");
        var exercise = input.TryGetProperty("exercise", out var e) ? e.GetString() : null;
        if (string.IsNullOrEmpty(exercise))
            throw new InvalidOperationException("Job de vídeo sem exercício informado.");

        var overlayKey = job.MediaKey[..job.MediaKey.LastIndexOf('.')] + "_overlay.mp4";

        var client = httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(options.Value.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);

        logger.LogInformation("Enviando job {JobId} ({Exercise}) ao serviço vision.", job.Id, exercise);
        using var response = await client.PostAsJsonAsync("/analyze", new
        {
            media_key = job.MediaKey,
            exercise,
            overlay_key = overlayKey,
        }, ct);

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
        {
            // Erro de negócio (vídeo longo demais, pose não detectada...) — sem retry.
            var detail = await ReadDetailAsync(response, ct);
            throw new InvalidOperationException(detail ?? "O vídeo não pôde ser analisado.");
        }
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<VisionAnalyzeResponse>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Resposta vazia do serviço vision.");

        var analysis = new ExerciseVideoAnalysis
        {
            UserId = job.UserId,
            AnalysisJobId = job.Id,
            MediaKey = job.MediaKey,
            OverlayVideoKey = result.OverlayKey,
            AnalyzedExercise = exercise,
            Score = result.Score,
            RepCount = result.RepCount,
            ResultJson = JsonSerializer.Serialize(new
            {
                issues = result.Issues,
                metrics = result.Metrics,
                notEvaluableReason = result.NotEvaluableReason,
            }, JsonOptions),
        };
        db.ExerciseVideoAnalyses.Add(analysis);
        await db.SaveChangesAsync(ct);
        return analysis.Id;
    }

    private static async Task<string?> ReadDetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            return body.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
