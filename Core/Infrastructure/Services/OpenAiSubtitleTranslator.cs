using System.ClientModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Infrastructure.Services;

public partial class OpenAiSubtitleTranslator(ILogger<OpenAiSubtitleTranslator> logger) : ISubtitleTranslator
{
    public async Task<string> TranslateSubtitlesAsync(string subtitlesContent, LlmSettingsDto settings)
    {
        if (string.IsNullOrEmpty(settings.ApiKey))
            throw new InvalidOperationException("LLM API key is not configured");

        var client = new OpenAIClient(
            new ApiKeyCredential(settings.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(settings.BaseUrl) });

        var chatClient = client.GetChatClient(settings.Model);

        logger.LogInformation("Sending translation request to {model} at {baseUrl}", settings.Model, settings.BaseUrl);

        var response = await chatClient.CompleteChatAsync(
        [
            new SystemChatMessage(settings.SystemPrompt),
            new UserChatMessage(subtitlesContent)
        ],
        new ChatCompletionOptions { Temperature = settings.Temperature, MaxOutputTokenCount = 65536 });

        if (response.Value.FinishReason == ChatFinishReason.ContentFilter)
        {
            throw new InvalidOperationException(
                "LLM blocked this translation due to content filtering policy.");
        }

        var text = response.Value.Content[0].Text;

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("LLM returned empty response");
        }

        text = MarkdownCodeBlockRegex().Replace(text, "").Trim();

        return text;
    }

    [GeneratedRegex(@"^```[\w-]*\n|```$", RegexOptions.Multiline)]
    private static partial Regex MarkdownCodeBlockRegex();
}
