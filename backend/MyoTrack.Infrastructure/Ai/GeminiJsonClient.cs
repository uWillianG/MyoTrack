using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MyoTrack.Infrastructure.Ai;

/// <summary>
/// ILlmJsonClient sobre a API do Google Gemini (Generative Language API,
/// chave do AI Studio). Usa REST direto — o corpo é pequeno e estável, e
/// evita depender do SDK do Google. Structured output via responseSchema.
/// </summary>
public class GeminiJsonClient(
    IOptions<LlmOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<GeminiJsonClient> logger) : ILlmJsonClient
{
    public const string HttpClientName = "gemini";
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly LlmOptions _options = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.GeminiApiKey);
    public string Model => _options.GeminiModel;

    public Task<LlmJsonResult?> GenerateJsonAsync(
        string systemPrompt, string userPrompt,
        Dictionary<string, JsonElement> jsonSchema, CancellationToken ct = default) =>
        CreateAsync(systemPrompt, userPrompt, jsonSchema, null, null, ct);

    public Task<LlmJsonResult?> GenerateJsonFromImageAsync(
        string systemPrompt, string userPrompt, byte[] imageBytes, string imageMediaType,
        Dictionary<string, JsonElement> jsonSchema, CancellationToken ct = default) =>
        CreateAsync(systemPrompt, userPrompt, jsonSchema, imageBytes, imageMediaType, ct);

    /// <summary>
    /// O responseSchema do Gemini é um subconjunto do OpenAPI 3.0: palavras-chave
    /// como additionalProperties/$schema causam 400. Remove o que não é suportado,
    /// preservando a estrutura — a validação forte continua no backend.
    /// </summary>
    public static JsonElement SanitizeSchema(JsonElement schema)
    {
        string[] allowed = ["type", "format", "description", "nullable", "enum", "items",
            "properties", "required", "minimum", "maximum", "minItems", "maxItems"];

        if (schema.ValueKind != JsonValueKind.Object)
            return schema;

        var result = new Dictionary<string, object?>();
        foreach (var property in schema.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                continue;

            result[property.Name] = property.Name switch
            {
                "items" => SanitizeSchema(property.Value),
                "properties" => property.Value.EnumerateObject()
                    .ToDictionary(p => p.Name, p => (object?)SanitizeSchema(p.Value)),
                _ => property.Value.Clone(),
            };
        }
        return JsonSerializer.SerializeToElement(result, JsonOptions);
    }

    private async Task<LlmJsonResult?> CreateAsync(
        string systemPrompt, string userPrompt, Dictionary<string, JsonElement> jsonSchema,
        byte[]? imageBytes, string? imageMediaType, CancellationToken ct)
    {
        if (!IsConfigured)
            return null;

        try
        {
            var parts = new List<object>();
            if (imageBytes is not null)
                parts.Add(new
                {
                    inline_data = new { mime_type = imageMediaType, data = Convert.ToBase64String(imageBytes) },
                });
            parts.Add(new { text = userPrompt });

            var schemaElement = SanitizeSchema(
                JsonSerializer.SerializeToElement(jsonSchema, JsonOptions));

            var body = new
            {
                system_instruction = new { parts = new[] { new { text = systemPrompt } } },
                contents = new[] { new { role = "user", parts } },
                generationConfig = new
                {
                    maxOutputTokens = _options.MaxTokens,
                    responseMimeType = "application/json",
                    responseSchema = schemaElement,
                },
            };

            var http = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"{BaseUrl}/{_options.GeminiModel}:generateContent")
            {
                Content = JsonContent.Create(body, options: JsonOptions),
            };
            request.Headers.Add("x-goog-api-key", _options.GeminiApiKey);

            var response = await http.SendAsync(request, ct);
            var payload = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Gemini respondeu {Status}: {Body}",
                    (int)response.StatusCode, payload[..Math.Min(payload.Length, 500)]);
                return null;
            }

            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;
            var text = root.GetProperty("candidates")[0]
                .GetProperty("content").GetProperty("parts")[0]
                .GetProperty("text").GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                logger.LogWarning("Resposta do Gemini sem texto.");
                return null;
            }

            long inputTokens = 0, outputTokens = 0;
            if (root.TryGetProperty("usageMetadata", out var usage))
            {
                if (usage.TryGetProperty("promptTokenCount", out var it)) inputTokens = it.GetInt64();
                if (usage.TryGetProperty("candidatesTokenCount", out var ot)) outputTokens = ot.GetInt64();
            }

            return new LlmJsonResult(text, inputTokens, outputTokens);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Falha de LLM nunca derruba a geração — o chamador cai no motor de regras.
            logger.LogError(ex, "Falha na chamada ao Gemini.");
            return null;
        }
    }
}
