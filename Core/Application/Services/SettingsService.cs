using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Services;

public class SettingsService(IAppSettingsRepository repository, IUnitOfWork unitOfWork) : ISettingsService
{
    public async Task<string?> GetSettingAsync(string key)
    {
        var setting = await repository.GetByKeyAsync(key);
        return setting?.Value;
    }

    public async Task UpdateSettingAsync(string key, string value)
    {
        var existingSetting = await repository.GetByKeyAsync(key);

        if (existingSetting != null)
        {
            existingSetting.Value = value;
            existingSetting.UpdatedAt = DateTime.UtcNow;
            await repository.UpdateAsync(existingSetting);
        }
        else
        {
            var newSetting = new AppSettingDto
            {
                Key = key,
                Value = value,
                UpdatedAt = DateTime.UtcNow
            };
            repository.Add(newSetting);
        }

        await unitOfWork.SaveChangesAsync();
    }

    public async Task<List<AppSettingDto>> GetAllSettingsAsync()
    {
        var settings = await repository.GetAllAsync();
        return settings.Select(s => new AppSettingDto
        {
            Id = s.Id,
            Key = s.Key,
            Value = s.Value,
            Description = s.Description,
            UpdatedAt = s.UpdatedAt
        }).ToList();
    }

    public async Task<LlmSettingsDto> GetLlmSettingsAsync()
    {
        var apiKey = await GetSettingAsync("LlmApiKey")
            ?? throw new ArgumentException("LlmApiKey setting not found");

        var baseUrl = await GetSettingAsync("LlmBaseUrl")
            ?? throw new ArgumentException("LlmBaseUrl setting not found");

        var model = await GetSettingAsync("LlmModel")
            ?? throw new ArgumentException("LlmModel setting not found");

        var systemPrompt = await GetSettingAsync("SystemPrompt")
            ?? throw new ArgumentException("SystemPrompt setting not found");

        var temperatureStr = await GetSettingAsync("Temperature")
            ?? throw new ArgumentException("Temperature setting not found");

        var temperature = float.TryParse(temperatureStr, out var temp)
            ? temp
            : throw new ArgumentException($"Invalid value for setting Temperature, value: {temperatureStr}");

        var maxOutputTokensStr = await GetSettingAsync("LlmMaxOutputTokens");
        var maxOutputTokens = int.TryParse(maxOutputTokensStr, out var mot) ? mot : 65535;

        var preferredLang = await GetSettingAsync("PreferredSubsLang")
            ?? throw new ArgumentException("PreferredSubsLang setting not found");

        var ocrBatchSizeStr = await GetSettingAsync("OcrBatchSize");
        var ocrBatchSize = int.TryParse(ocrBatchSizeStr, out var obs) ? obs : 15;

        return new LlmSettingsDto
        {
            ApiKey = apiKey,
            BaseUrl = baseUrl,
            Model = model,
            SystemPrompt = systemPrompt,
            Temperature = temperature,
            MaxOutputTokens = maxOutputTokens,
            PreferredSubsLang = preferredLang,
            OcrBatchSize = ocrBatchSize,
        };
    }

    public async Task<LlmSettingsDto> GetOcrLlmSettingsAsync()
    {
        var ocrApiKey = await GetSettingAsync("OcrLlmApiKey");
        var apiKey = !string.IsNullOrWhiteSpace(ocrApiKey)
            ? ocrApiKey
            : await GetSettingAsync("LlmApiKey") ?? throw new ArgumentException("LlmApiKey setting not found");

        var ocrBaseUrl = await GetSettingAsync("OcrLlmBaseUrl");
        var baseUrl = !string.IsNullOrWhiteSpace(ocrBaseUrl)
            ? ocrBaseUrl
            : await GetSettingAsync("LlmBaseUrl") ?? throw new ArgumentException("LlmBaseUrl setting not found");

        var ocrModel = await GetSettingAsync("OcrLlmModel");
        var model = !string.IsNullOrWhiteSpace(ocrModel)
            ? ocrModel
            : await GetSettingAsync("LlmModel") ?? throw new ArgumentException("LlmModel setting not found");

        var ocrTempStr = await GetSettingAsync("OcrTemperature");
        var temperature = float.TryParse(ocrTempStr, System.Globalization.CultureInfo.InvariantCulture, out var ocrTemp)
            ? ocrTemp
            : 0f;

        var ocrMaxTokensStr = await GetSettingAsync("OcrMaxOutputTokens");
        int maxOutputTokens;
        if (!string.IsNullOrWhiteSpace(ocrMaxTokensStr) && int.TryParse(ocrMaxTokensStr, out var ocrMaxTokens))
            maxOutputTokens = ocrMaxTokens;
        else
        {
            var mainMaxStr = await GetSettingAsync("LlmMaxOutputTokens");
            maxOutputTokens = int.TryParse(mainMaxStr, out var mainMax) ? mainMax : 65535;
        }

        var ocrSystemPrompt = await GetSettingAsync("OcrSystemPrompt") ?? "";
        var preferredLang = await GetSettingAsync("PreferredSubsLang") ?? "pl";
        var batchSizeStr = await GetSettingAsync("OcrBatchSize");
        var batchSize = int.TryParse(batchSizeStr, out var bs) ? bs : 15;

        return new LlmSettingsDto
        {
            ApiKey = apiKey,
            BaseUrl = baseUrl,
            Model = model,
            SystemPrompt = ocrSystemPrompt,
            Temperature = temperature,
            MaxOutputTokens = maxOutputTokens,
            PreferredSubsLang = preferredLang,
            OcrBatchSize = batchSize
        };
    }
}

