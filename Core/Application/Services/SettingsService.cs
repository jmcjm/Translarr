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

    public async Task<GeminiSettingsDto> GetGeminiSettingsAsync()
    {
        var apiKey = await GetSettingAsync("GeminiApiKey")
            ?? throw new ArgumentException("GeminiApiKey setting not found");

        var model = await GetSettingAsync("GeminiModel")
            ?? throw new ArgumentException("GeminiModel setting not found");

        var systemPrompt = await GetSettingAsync("SystemPrompt")
            ?? throw new ArgumentException("SystemPrompt setting not found");

        var temperatureStr = await GetSettingAsync("Temperature")
            ?? throw new ArgumentException("Temperature setting not found");

        var temperature = float.TryParse(temperatureStr, out var temp)
            ? temp
            : throw new ArgumentException($"Invalid value for setting Temperature, value: {temperatureStr}");

        var preferredLang = await GetSettingAsync("PreferredSubsLang")
            ?? throw new ArgumentException("PreferredSubsLang setting not found");

        return new GeminiSettingsDto
        {
            ApiKey = apiKey,
            Model = model,
            SystemPrompt = systemPrompt,
            Temperature = temperature,
            PreferredSubsLang = preferredLang
        };
    }
}

