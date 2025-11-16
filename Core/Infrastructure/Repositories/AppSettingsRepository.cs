using Microsoft.EntityFrameworkCore;
using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Models;
using Translarr.Core.Infrastructure.Persistence;
using Translarr.Core.Infrastructure.Persistence.Daos;

namespace Translarr.Core.Infrastructure.Repositories;

public class AppSettingsRepository(TranslarrDbContext context) : IAppSettingsRepository
{
    public async Task<AppSettingDto?> GetByKeyAsync(string key)
    {
        var setting = await context.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key);

        return setting == null ? null : MapToDto(setting);
    }

    public async Task<List<AppSettingDto>> GetAllAsync()
    {
        var settings = await context.AppSettings
            .AsNoTracking()
            .ToListAsync();
        return settings.Select(MapToDto).ToList();
    }

    public void Add(AppSettingDto setting)
    {
        var dao = MapToDao(setting);
        context.AppSettings.Add(dao);
        setting.Id = dao.Id;
    }

    public async Task UpdateAsync(AppSettingDto setting)
    {
        var dao = await context.AppSettings.FindAsync(setting.Id);

        if (dao == null)
        {
            throw new InvalidOperationException($"AppSetting with Id {setting.Id} not found");
        }

        dao.Key = setting.Key;
        dao.Value = setting.Value;
        dao.Description = setting.Description;
        dao.UpdatedAt = setting.UpdatedAt;
    }

    private static AppSettingDto MapToDto(AppSettingsDao dao)
    {
        return new AppSettingDto
        {
            Id = dao.Id,
            Key = dao.Key,
            Value = dao.Value,
            Description = dao.Description,
            UpdatedAt = dao.UpdatedAt
        };
    }

    private static AppSettingsDao MapToDao(AppSettingDto dto)
    {
        return new AppSettingsDao
        {
            Id = dto.Id,
            Key = dto.Key,
            Value = dto.Value,
            Description = dto.Description,
            UpdatedAt = dto.UpdatedAt
        };
    }
}

