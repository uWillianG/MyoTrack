using System.Text.Json;

namespace MyoTrack.Infrastructure.Ai;

public record LlmJsonResult(string Json, long InputTokens, long OutputTokens);

/// <summary>Chamada de LLM com structured output (JSON Schema). Retorna null quando não configurado ou em falha.</summary>
public interface ILlmJsonClient
{
    bool IsConfigured { get; }
    string Model { get; }

    Task<LlmJsonResult?> GenerateJsonAsync(
        string systemPrompt,
        string userPrompt,
        Dictionary<string, JsonElement> jsonSchema,
        CancellationToken ct = default);

    Task<LlmJsonResult?> GenerateJsonFromImageAsync(
        string systemPrompt,
        string userPrompt,
        byte[] imageBytes,
        string imageMediaType,
        Dictionary<string, JsonElement> jsonSchema,
        CancellationToken ct = default);
}
