using System.Text.Json;

namespace MyoTrack.Infrastructure.Ai;

/// <summary>Chamada de LLM com structured output (JSON Schema). Retorna null quando não configurado.</summary>
public interface ILlmJsonClient
{
    bool IsConfigured { get; }

    Task<string?> GenerateJsonAsync(
        string systemPrompt,
        string userPrompt,
        Dictionary<string, JsonElement> jsonSchema,
        CancellationToken ct = default);
}
