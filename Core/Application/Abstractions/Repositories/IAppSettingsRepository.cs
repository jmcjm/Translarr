using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Repositories;

public interface IAppSettingsRepository
{
    Task<AppSettingDto?> GetByKeyAsync(string key);
    Task<List<AppSettingDto>> GetAllAsync();
    void Add(AppSettingDto setting);
    Task UpdateAsync(AppSettingDto setting);
}

