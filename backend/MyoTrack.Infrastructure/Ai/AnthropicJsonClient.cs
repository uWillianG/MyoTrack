using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MyoTrack.Infrastructure.Ai;

public class AnthropicJsonClient(IOptions<LlmOptions> options, ILogger<AnthropicJsonClient> logger) : ILlmJsonClient
{
    private readonly LlmOptions _options = options.Value;
    private readonly AnthropicClient? _client =
        string.IsNullOrWhiteSpace(options.Value.AnthropicApiKey)
            ? null
            : new AnthropicClient { ApiKey = options.Value.AnthropicApiKey };

    public bool IsConfigured => _client is not null;
    public string Model => _options.Model;

    public Task<LlmJsonResult?> GenerateJsonAsync(
        string systemPrompt, string userPrompt,
        Dictionary<string, JsonElement> jsonSchema, CancellationToken ct = default) =>
        CreateAsync(systemPrompt, userPrompt, jsonSchema, null, ct);

    public Task<LlmJsonResult?> GenerateJsonFromImageAsync(
        string systemPrompt, string userPrompt, byte[] imageBytes, string imageMediaType,
        Dictionary<string, JsonElement> jsonSchema, CancellationToken ct = default) =>
        CreateAsync(systemPrompt, userPrompt, jsonSchema,
            new ImageBlockParam
            {
                Source = new Base64ImageSource
                {
                    Data = Convert.ToBase64String(imageBytes),
                    MediaType = imageMediaType,
                },
            }, ct);

    private async Task<LlmJsonResult?> CreateAsync(
        string systemPrompt, string userPrompt,
        Dictionary<string, JsonElement> jsonSchema, ImageBlockParam? image, CancellationToken ct)
    {
        if (_client is null)
            return null;

        try
        {
            List<ContentBlockParam> content = [];
            if (image is not null)
                content.Add(image);
            content.Add(new TextBlockParam { Text = userPrompt });

            var response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _options.Model,
                MaxTokens = _options.MaxTokens,
                System = systemPrompt,
                OutputConfig = new OutputConfig
                {
                    Format = new JsonOutputFormat { Schema = jsonSchema },
                },
                Messages = [new() { Role = Role.User, Content = content }],
            });

            foreach (var block in response.Content)
            {
                if (block.TryPickText(out TextBlock? text))
                    return new LlmJsonResult(text.Text, response.Usage.InputTokens, response.Usage.OutputTokens);
            }

            logger.LogWarning("Resposta do LLM sem bloco de texto (stop_reason={StopReason}).", response.StopReason);
            return null;
        }
        catch (Exception ex)
        {
            // Falha de LLM nunca derruba a geração — o chamador cai no motor de regras.
            logger.LogError(ex, "Falha na chamada ao LLM.");
            return null;
        }
    }
}
