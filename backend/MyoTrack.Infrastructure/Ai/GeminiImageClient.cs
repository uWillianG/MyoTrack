using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MyoTrack.Infrastructure.Ai;

public record GeneratedImage(byte[] Bytes, string MediaType, long InputTokens, long OutputTokens);

/// <summary>
/// Edição de imagem via Gemini (análise ilustrada): recebe a foto original e
/// uma instrução, devolve a imagem anotada. Falha nunca derruba o chamador —
/// retorna null e a análise segue no modo padrão.
/// </summary>
public class GeminiImageClient(
    IOptions<LlmOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<GeminiImageClient> logger)
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly LlmOptions _options = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.GeminiApiKey);
    public string Model => _options.GeminiImageModel;

    public async Task<GeneratedImage?> EditImageAsync(
        byte[] imageBytes, string imageMediaType, string instruction, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return null;

        try
        {
            var body = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new object[]
                        {
                            new { inline_data = new { mime_type = imageMediaType, data = Convert.ToBase64String(imageBytes) } },
                            new { text = instruction },
                        },
                    },
                },
                generationConfig = new { responseModalities = new[] { "TEXT", "IMAGE" } },
            };

            var http = httpClientFactory.CreateClient(GeminiJsonClient.HttpClientName);
            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"{BaseUrl}/{_options.GeminiImageModel}:generateContent")
            {
                Content = JsonContent.Create(body, options: JsonOptions),
            };
            request.Headers.Add("x-goog-api-key", _options.GeminiApiKey);

            var response = await http.SendAsync(request, ct);
            var payload = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Gemini (imagem) respondeu {Status}: {Body}",
                    (int)response.StatusCode, payload[..Math.Min(payload.Length, 500)]);
                return null;
            }

            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;
            if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0
                || !candidates[0].TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var parts))
            {
                logger.LogWarning("Resposta do Gemini (imagem) sem candidates/parts.");
                return null;
            }

            long inputTokens = 0, outputTokens = 0;
            if (root.TryGetProperty("usageMetadata", out var usage))
            {
                if (usage.TryGetProperty("promptTokenCount", out var it)) inputTokens = it.GetInt64();
                if (usage.TryGetProperty("candidatesTokenCount", out var ot)) outputTokens = ot.GetInt64();
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (!part.TryGetProperty("inlineData", out var inline))
                    continue;
                var data = inline.GetProperty("data").GetString();
                if (string.IsNullOrEmpty(data))
                    continue;
                var mediaType = inline.TryGetProperty("mimeType", out var mime)
                    ? mime.GetString() ?? "image/png"
                    : "image/png";
                return new GeneratedImage(Convert.FromBase64String(data), mediaType, inputTokens, outputTokens);
            }

            logger.LogWarning("Resposta do Gemini (imagem) sem bloco de imagem.");
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha na geração da imagem ilustrada.");
            return null;
        }
    }
}
