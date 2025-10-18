using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Services;

public class SettingsService(IAppSettingsRepository repository) : ISettingsService
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
            await repository.AddAsync(newSetting);
        }
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
}

