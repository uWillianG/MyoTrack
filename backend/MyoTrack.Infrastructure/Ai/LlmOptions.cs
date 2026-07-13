namespace MyoTrack.Infrastructure.Ai;

public class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>Vazio ⇒ geração usa apenas o motor de regras (sem chamada externa).</summary>
    public string? AnthropicApiKey { get; set; }
    public string Model { get; set; } = "claude-opus-4-8";
    public int MaxTokens { get; set; } = 4096;
}
