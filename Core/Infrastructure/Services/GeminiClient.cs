using Mscc.GenerativeAI;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Infrastructure.Services;

public class GeminiClient : IGeminiClient
{
    public async Task<string> TranslateSubtitlesAsync(string subtitlesContent, GeminiSettingsDto settings)
    {
        if (string.IsNullOrEmpty(settings.ApiKey))
        {
            throw new InvalidOperationException("GeminiApiKey setting is not configured");
        }

        var googleAi = new GoogleAI(apiKey: settings.ApiKey);
        var systemInstruction = new Content(settings.SystemPrompt);
        var generativeModel = googleAi.GenerativeModel(model: settings.Model, systemInstruction: systemInstruction);

        var generationConfig = new GenerationConfig
        {
            Temperature = settings.Temperature
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
}