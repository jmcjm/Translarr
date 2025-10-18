using Mscc.GenerativeAI;
using Translarr.Core.Application.Abstractions.Services;

namespace Translarr.Core.Infrastructure.Services;

public class GeminiClient(ISettingsService settingsService) : IGeminiClient
{
    public async Task<string> TranslateSubtitlesAsync(string subtitlesContent, string systemPrompt, float temperature, string model)
    {
        var apiKey = await settingsService.GetSettingAsync("GeminiApiKey");
        
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("GeminiApiKey setting is not configured");
        }

        var googleAi = new GoogleAI(apiKey: apiKey);
        var systemInstruction = new Content(systemPrompt);
        var generativeModel = googleAi.GenerativeModel(model: model, systemInstruction: systemInstruction);
        
        var generationConfig = new GenerationConfig
        {
            Temperature = temperature
        };

        var response = await generativeModel.GenerateContent(subtitlesContent, generationConfig: generationConfig);
        
        if (response.Text == null)
        {
            throw new InvalidOperationException("Gemini API returned empty response");
        }

        return response.Text;
    }
}

