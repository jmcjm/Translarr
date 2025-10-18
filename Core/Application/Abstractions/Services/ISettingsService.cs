using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Services;

public interface ISettingsService
{
    Task<string?> GetSettingAsync(string key);
    Task UpdateSettingAsync(string key, string value);
    Task<List<AppSettingDto>> GetAllSettingsAsync();
}

