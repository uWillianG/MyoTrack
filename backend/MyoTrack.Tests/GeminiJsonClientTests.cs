using System.Text.Json;
using MyoTrack.Infrastructure.Ai;

namespace MyoTrack.Tests;

public class GeminiJsonClientTests
{
    [Fact]
    public void SanitizeSchema_RemovesUnsupportedKeywords_KeepsStructure()
    {
        const string schema = """
            {
              "type": "object",
              "$schema": "http://json-schema.org/draft-07/schema#",
              "additionalProperties": false,
              "properties": {
                "days": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "properties": {
                      "order": { "type": "integer" },
                      "label": { "type": "string" }
                    },
                    "required": ["order", "label"]
                  }
                }
              },
              "required": ["days"]
            }
            """;

        var sanitized = GeminiJsonClient.SanitizeSchema(JsonSerializer.Deserialize<JsonElement>(schema));
        var text = sanitized.GetRawText();

        Assert.DoesNotContain("additionalProperties", text);
        Assert.DoesNotContain("$schema", text);
        // Estrutura preservada, inclusive dentro de items aninhados.
        Assert.Equal("integer", sanitized
            .GetProperty("properties").GetProperty("days")
            .GetProperty("items").GetProperty("properties")
            .GetProperty("order").GetProperty("type").GetString());
        Assert.Equal(2, sanitized
            .GetProperty("properties").GetProperty("days")
            .GetProperty("items").GetProperty("required").GetArrayLength());
    }

    [Theory]
    [InlineData("gemini", "chave-a", "chave-g", "gemini")]       // explícito vence
    [InlineData("Anthropic", null, "chave-g", "anthropic")]      // explícito, case-insensitive
    [InlineData(null, "chave-a", null, "anthropic")]             // autodetecção
    [InlineData(null, null, "chave-g", "gemini")]                // autodetecção
    [InlineData(null, "chave-a", "chave-g", "anthropic")]        // ambas ⇒ anthropic
    [InlineData(null, null, null, "anthropic")]                  // nenhuma ⇒ padrão (não configurado)
    public void EffectiveProvider_SelectsByConfigOrKeys(
        string? provider, string? anthropicKey, string? geminiKey, string expected)
    {
        var options = new LlmOptions
        {
            Provider = provider,
            AnthropicApiKey = anthropicKey,
            GeminiApiKey = geminiKey,
        };
        Assert.Equal(expected, options.EffectiveProvider);
    }
}
