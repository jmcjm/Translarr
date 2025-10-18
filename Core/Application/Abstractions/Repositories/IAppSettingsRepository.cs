using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Repositories;

public interface IAppSettingsRepository
{
    Task<AppSettingDto?> GetByKeyAsync(string key);
    Task<List<AppSettingDto>> GetAllAsync();
    Task AddAsync(AppSettingDto setting);
    Task UpdateAsync(AppSettingDto setting);
}

