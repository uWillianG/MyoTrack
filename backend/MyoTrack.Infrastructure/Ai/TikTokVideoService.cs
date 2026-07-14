using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MyoTrack.Infrastructure.Ai;

/// <summary>
/// Resolve um vídeo explicativo do TikTok para cada exercício do catálogo,
/// uma única vez: o link fica salvo em Exercise.TutorialVideoUrl e é
/// compartilhado por todos os usuários. Best-effort — o TikTok não tem API
/// pública de busca, então o serviço lê o HTML da página de busca e valida
/// o primeiro vídeo encontrado via oEmbed (API oficial). Se nada for
/// encontrado, o exercício fica sem link e o frontend cai para a URL de busca.
/// </summary>
public partial class TikTokVideoService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<TikTokVideoService> logger)
{
    public const string HttpClientName = "tiktok";

    /// <summary>Máximo de buscas por geração de treino, para não atrasar o job.</summary>
    private const int MaxLookupsPerRun = 12;

    [GeneratedRegex(@"https://www\.tiktok\.com/@[A-Za-z0-9._\-]+/video/\d+", RegexOptions.CultureInvariant)]
    private static partial Regex VideoUrlRegex();

    public static string BuildSearchQuery(string exerciseName) => $"como fazer {exerciseName} academia";

    public static string BuildSearchUrl(string exerciseName) =>
        $"https://www.tiktok.com/search?q={Uri.EscapeDataString(BuildSearchQuery(exerciseName))}";

    /// <summary>
    /// Extrai a primeira URL de vídeo do HTML da página de busca. O JSON embutido
    /// pelo TikTok costuma escapar as barras como /, então normaliza antes.
    /// </summary>
    public static string? ExtractFirstVideoUrl(string html)
    {
        var normalized = html
            .Replace("\\u002F", "/", StringComparison.OrdinalIgnoreCase)
            .Replace("\\/", "/", StringComparison.Ordinal);
        var match = VideoUrlRegex().Match(normalized);
        return match.Success ? match.Value : null;
    }

    /// <summary>Preenche TutorialVideoUrl dos exercícios que ainda não têm vídeo salvo.</summary>
    public async Task ResolveMissingAsync(IReadOnlyCollection<int> exerciseIds, CancellationToken ct = default)
    {
        var pending = await db.Exercises
            .Where(e => exerciseIds.Contains(e.Id) && e.TutorialVideoUrl == null)
            .Take(MaxLookupsPerRun)
            .ToListAsync(ct);
        if (pending.Count == 0)
            return;

        var http = httpClientFactory.CreateClient(HttpClientName);
        foreach (var exercise in pending)
        {
            try
            {
                var url = await ResolveOneAsync(http, exercise.Name, ct);
                if (url is null)
                {
                    logger.LogInformation("Nenhum vídeo do TikTok encontrado para '{Exercise}'.", exercise.Name);
                    continue;
                }

                exercise.TutorialVideoUrl = url;
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Vídeo salvo para '{Exercise}': {Url}", exercise.Name, url);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Falha de rede/bloqueio do TikTok não pode derrubar a geração do treino.
                logger.LogWarning(ex, "Falha ao buscar vídeo do TikTok para '{Exercise}'.", exercise.Name);
            }
        }
    }

    private static async Task<string?> ResolveOneAsync(HttpClient http, string exerciseName, CancellationToken ct)
    {
        var response = await http.GetAsync(BuildSearchUrl(exerciseName), ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var candidate = ExtractFirstVideoUrl(await response.Content.ReadAsStringAsync(ct));
        if (candidate is null)
            return null;

        // oEmbed é a API oficial: confirma que o vídeo existe e é público.
        var oembed = await http.GetAsync(
            $"https://www.tiktok.com/oembed?url={Uri.EscapeDataString(candidate)}", ct);
        if (!oembed.IsSuccessStatusCode)
            return null;

        using var json = JsonDocument.Parse(await oembed.Content.ReadAsStringAsync(ct));
        return json.RootElement.TryGetProperty("title", out _) ? candidate : null;
    }
}
