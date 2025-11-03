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

        if (response.Text == null)
        {
            throw new InvalidOperationException("Gemini API returned empty response");
        }

        return response.Text;
    }
}

