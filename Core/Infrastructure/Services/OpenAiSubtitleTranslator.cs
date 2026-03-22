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
        var chatClient = CreateChatClient(settings);

        logger.LogInformation("Sending translation request to {model} at {baseUrl}", settings.Model, settings.BaseUrl);

        var response = await chatClient.CompleteChatAsync(
        [
            new SystemChatMessage(settings.SystemPrompt),
            new UserChatMessage(subtitlesContent)
        ],
        new ChatCompletionOptions { Temperature = settings.Temperature, MaxOutputTokenCount = settings.MaxOutputTokens });

        return ValidateAndExtractResponse(response.Value);
    }

    public async Task<string> TranslateWithImagesAsync(string prompt, List<byte[]> images, LlmSettingsDto settings)
    {
        var chatClient = CreateChatClient(settings);

        logger.LogInformation("Sending OCR request with {count} images to {model}", images.Count, settings.Model);

        var parts = new List<ChatMessageContentPart>();
        parts.Add(ChatMessageContentPart.CreateTextPart(prompt));

        foreach (var imageBytes in images)
        {
            var binaryData = BinaryData.FromBytes(imageBytes);
            parts.Add(ChatMessageContentPart.CreateImagePart(binaryData, "image/png"));
        }

        var response = await chatClient.CompleteChatAsync(
            [new UserChatMessage(parts)],
            new ChatCompletionOptions { Temperature = settings.Temperature, MaxOutputTokenCount = settings.MaxOutputTokens });

        return ValidateAndExtractResponse(response.Value);
    }

    private ChatClient CreateChatClient(LlmSettingsDto settings)
    {
        if (string.IsNullOrEmpty(settings.ApiKey))
            throw new InvalidOperationException("LLM API key is not configured");

        var client = new OpenAIClient(
            new ApiKeyCredential(settings.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(settings.BaseUrl) });

        return client.GetChatClient(settings.Model);
    }

    private string ValidateAndExtractResponse(ChatCompletion response)
    {
        if (response.FinishReason == ChatFinishReason.ContentFilter)
            throw new InvalidOperationException("LLM blocked this request due to content filtering policy.");

        var text = response.Content[0].Text;

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("LLM returned empty response");

        return MarkdownCodeBlockRegex().Replace(text, "").Trim();
    }

    [GeneratedRegex(@"^```[\w-]*\n|```$", RegexOptions.Multiline)]
    private static partial Regex MarkdownCodeBlockRegex();
}
