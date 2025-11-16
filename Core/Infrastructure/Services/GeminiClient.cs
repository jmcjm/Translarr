using System.Text;
using Microsoft.Extensions.Logging;
using Mscc.GenerativeAI;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Infrastructure.Services;

public class GeminiClient(ILogger<GeminiClient> logger) : IGeminiClient
{
    public async Task<string> TranslateSubtitlesAsync(string subtitlesContent, GeminiSettingsDto settings)
    {
        if (string.IsNullOrEmpty(settings.ApiKey))
        {
            throw new InvalidOperationException("GeminiApiKey setting is not configured");
        }

        var googleAi = new GoogleAI(apiKey: settings.ApiKey, logger: logger);
        var systemInstruction = new Content(settings.SystemPrompt);
        var generativeModel = googleAi.GenerativeModel(model: settings.Model, systemInstruction: systemInstruction);

        var generationConfig = new GenerationConfig
        {
            Temperature = settings.Temperature,
            MaxOutputTokens = 65536
        };

        var response = await generativeModel.GenerateContent(subtitlesContent, generationConfig: generationConfig);

        // Gemini filters can sometimes block translations for "safety" reasons, we need to check for that to avoid silent failures
        // where the translation seems successful, but the outcoming srt file is empty.
        if (response.PromptFeedback != null)
        {
            var blockReason = response.PromptFeedback.BlockReason;
            var blockReasonMessage = response.PromptFeedback.BlockReasonMessage;
            
            throw new InvalidOperationException($"Gemini API blocked this translation and returned prompt feedback: {blockReason} - {blockReasonMessage}");
        }
        
        // Theoretically, the response.Text should never be null or empty if the API wasn't blocked for safety reasons
        if (response.Text == null || string.IsNullOrWhiteSpace(response.Text))
        {
            throw new InvalidOperationException("Gemini API returned empty response");
        }

        return response.Text;
    }

    public async Task<string> TranslateLargeSubtitlesAsync(string subtitlesContent, GeminiSettingsDto settings)
    {
        // It's not ready yet and not fully tested
        throw new NotImplementedException("Translating such large subtitles is not fully supported yet");
        
#pragma warning disable CS0162 // Unreachable code detected
        if (string.IsNullOrEmpty(settings.ApiKey))
        {
            throw new InvalidOperationException("GeminiApiKey setting is not configured");
        }

        var googleAi = new GoogleAI(apiKey: settings.ApiKey, logger: logger);
        var systemInstruction = new Content(settings.SystemPrompt);
        var generativeModel = googleAi.GenerativeModel(model: settings.Model, systemInstruction: systemInstruction);

        var generationConfig = new GenerationConfig
        {
            Temperature = settings.Temperature,
            MaxOutputTokens = 65536
        };

        var chat = generativeModel.StartChat(generationConfig: generationConfig);
        
        // We have to check after every message if the previous answer was cut due to the output token limit
        // also my tests show that after the "continue" message, the model often make mistakes in lines numbers
        await chat.SendMessage(subtitlesContent);
        await chat.SendMessage("Continue the translation from the last line.");

        StringBuilder sb = new();
        
        foreach (var response in chat.History.Where(c => c.Role == Role.Model))
        {
            sb.AppendLine(response.Text);
        }
        
        return sb.ToString();
#pragma warning restore CS0162 // Unreachable code detected
    }
}