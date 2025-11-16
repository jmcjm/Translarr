using ErrorOr;
using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Services;

public class SeriesWatchService(
    ISeriesWatchConfigRepository watchConfigRepository,
    ISubtitleEntryRepository subtitleEntryRepository,
    IUnitOfWork unitOfWork) : ISeriesWatchService
{
    /// <inheritdoc />
    public async Task<ErrorOr<int>> SetAutoWatchAsync(string seriesName, string? seasonName, bool autoWatch)
    {
        if (autoWatch)
        {
            // Enable watch: add config + bulk mark as wanted
            var config = new SeriesWatchConfigDto
            {
                SeriesName = seriesName,
                SeasonName = seasonName,
                AutoWatch = true,
                CreatedAt = DateTime.UtcNow
            };

            var addResult = await watchConfigRepository.AddAsync(config);
            if (addResult.IsError)
                return addResult.Errors;

            await unitOfWork.SaveChangesAsync();

            // Bulk mark existing files as wanted (ExecuteUpdateAsync saves automatically)
            var updatedCount = await subtitleEntryRepository.BulkUpdateWantedAsync(seriesName, seasonName, true);
            return updatedCount;
        }
        else
        {
            // Disable watch: remove config (don't touch IsWanted on existing files)
            var deleted = await watchConfigRepository.DeleteAsync(seriesName, seasonName);
            if (deleted)
                await unitOfWork.SaveChangesAsync();

            return deleted ? 0 : Error.NotFound("SeriesWatchService.SetAutoWatchAsync",
                $"Watch configuration not found for series '{seriesName}'" +
                (seasonName != null ? $" season '{seasonName}'" : ""));
        }
    }

    /// <inheritdoc />
    public async Task<ErrorOr<bool>> RemoveAutoWatchAsync(string seriesName, string? seasonName)
    {
        var deleted = await watchConfigRepository.DeleteAsync(seriesName, seasonName);
        if (deleted)
            await unitOfWork.SaveChangesAsync();

        return deleted;
    }

    /// <inheritdoc />
    public async Task<List<SeriesWatchConfigDto>> GetAllWatchConfigsAsync()
    {
        return await watchConfigRepository.GetAllAsync();
    }

    /// <inheritdoc />
    public async Task<bool> ShouldAutoMarkWantedAsync(string seriesName, string seasonName)
    {
        return await watchConfigRepository.IsWatchedAsync(seriesName, seasonName);
    }

    /// <inheritdoc />
    public async Task<List<SeriesGroupDto>> GetSeriesGroupsWithWatchStatusAsync()
    {
        var seriesGroups = await subtitleEntryRepository.GetSeriesGroupsAsync();
        var watchConfigs = await watchConfigRepository.GetAllAsync();

        // Build a lookup for watch status
        var seriesWatchLookup = watchConfigs
            .Where(c => c.SeasonName == null)
            .ToDictionary(c => c.SeriesName, c => c.AutoWatch);

        var seasonWatchLookup = watchConfigs
            .Where(c => c.SeasonName != null)
            .ToDictionary(c => (c.SeriesName, c.SeasonName!), c => c.AutoWatch);

        // Populate watch status
        foreach (var series in seriesGroups)
        {
            // Series-level watch
            series.IsWatched = seriesWatchLookup.GetValueOrDefault(series.SeriesName, false);

            // Season-level watch
            foreach (var season in series.Seasons)
            {
                // If series is watched, all seasons are considered watched
                if (series.IsWatched)
                {
                    season.IsWatched = true;
                }
                else
                {
                    // Check season-specific watch
                    season.IsWatched = seasonWatchLookup.GetValueOrDefault(
                        (series.SeriesName, season.SeasonName), false);
                }
            }
        }

        return seriesGroups;
    }
}
