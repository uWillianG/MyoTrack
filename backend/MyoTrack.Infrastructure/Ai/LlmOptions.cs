namespace MyoTrack.Infrastructure.Ai;

public class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>
    /// "anthropic" ou "gemini". Vazio ⇒ autodetecção: usa o provider cuja
    /// chave de API estiver preenchida (Anthropic tem precedência se ambas).
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>Vazio ⇒ geração usa apenas o motor de regras (sem chamada externa).</summary>
    public string? AnthropicApiKey { get; set; }
    public string Model { get; set; } = "claude-opus-4-8";

    /// <summary>Chave da API do Google (Gemini) — AI Studio / Generative Language API.</summary>
    public string? GeminiApiKey { get; set; }
    public string GeminiModel { get; set; } = "gemini-3.5-flash";

    // Nos modelos com raciocínio (Gemini 3.x), os tokens de "thinking" contam
    // dentro deste teto — 4096 truncava respostas antes do texto final.
    public int MaxTokens { get; set; } = 8192;

    public string EffectiveProvider =>
        !string.IsNullOrWhiteSpace(Provider) ? Provider.Trim().ToLowerInvariant()
        : !string.IsNullOrWhiteSpace(AnthropicApiKey) ? "anthropic"
        : !string.IsNullOrWhiteSpace(GeminiApiKey) ? "gemini"
        : "anthropic";
}
