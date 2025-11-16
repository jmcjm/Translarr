using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Models;
using Translarr.Core.Infrastructure.Persistence;
using Translarr.Core.Infrastructure.Persistence.Daos;

namespace Translarr.Core.Infrastructure.Repositories;

public class SeriesWatchConfigRepository(TranslarrDbContext context) : ISeriesWatchConfigRepository
{
    /// <inheritdoc />
    public async Task<List<SeriesWatchConfigDto>> GetAllAsync()
    {
        var configs = await context.SeriesWatchConfigs
            .AsNoTracking()
            .ToListAsync();

        return configs.Select(MapToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<SeriesWatchConfigDto?> GetAsync(string seriesName, string? seasonName)
    {
        var config = await context.SeriesWatchConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.SeriesName == seriesName && c.SeasonName == seasonName);

        return config == null ? null : MapToDto(config);
    }

    /// <inheritdoc />
    public async Task<bool> IsWatchedAsync(string seriesName, string seasonName)
    {
        // Check series-level watch first (covers all seasons)
        var hasSeriesWatch = await context.SeriesWatchConfigs
            .AsNoTracking()
            .AnyAsync(c => c.SeriesName == seriesName && c.SeasonName == null && c.AutoWatch);

        if (hasSeriesWatch)
            return true;

        // Check season-specific watch
        return await context.SeriesWatchConfigs
            .AsNoTracking()
            .AnyAsync(c => c.SeriesName == seriesName && c.SeasonName == seasonName && c.AutoWatch);
    }

    /// <inheritdoc />
    public async Task<ErrorOr<SeriesWatchConfigDto>> AddAsync(SeriesWatchConfigDto config)
    {
        // Check if already exists
        var existing = await context.SeriesWatchConfigs
            .FirstOrDefaultAsync(c => c.SeriesName == config.SeriesName && c.SeasonName == config.SeasonName);

        if (existing != null)
        {
            return Error.Conflict(
                "SeriesWatchConfigRepository.Add",
                $"Watch configuration already exists for series '{config.SeriesName}'" +
                (config.SeasonName != null ? $" season '{config.SeasonName}'" : ""));
        }

        var dao = MapToDao(config);
        dao.CreatedAt = DateTime.UtcNow;

        context.SeriesWatchConfigs.Add(dao);

        config.Id = dao.Id;
        config.CreatedAt = dao.CreatedAt;

        return config;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string seriesName, string? seasonName)
    {
        var config = await context.SeriesWatchConfigs
            .FirstOrDefaultAsync(c => c.SeriesName == seriesName && c.SeasonName == seasonName);

        if (config == null)
            return false;

        context.SeriesWatchConfigs.Remove(config);

        return true;
    }

    private static SeriesWatchConfigDto MapToDto(SeriesWatchConfigDao dao)
    {
        return new SeriesWatchConfigDto
        {
            Id = dao.Id,
            SeriesName = dao.SeriesName,
            SeasonName = dao.SeasonName,
            AutoWatch = dao.AutoWatch,
            CreatedAt = dao.CreatedAt
        };
    }

    private static SeriesWatchConfigDao MapToDao(SeriesWatchConfigDto dto)
    {
        return new SeriesWatchConfigDao
        {
            Id = dto.Id,
            SeriesName = dto.SeriesName,
            SeasonName = dto.SeasonName,
            AutoWatch = dto.AutoWatch,
            CreatedAt = dto.CreatedAt
        };
    }
}
