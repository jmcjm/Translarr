using ErrorOr;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Services;

public interface ISeriesWatchService
{
    /// <summary>
    /// Sets auto-watch for a series or season. This will:
    /// 1. Create/update watch configuration
    /// 2. Mark all existing files in the scope as wanted
    /// 3. Future scans will auto-mark new files as wanted
    /// </summary>
    /// <param name="seriesName">The name of the series.</param>
    /// <param name="seasonName">The name of the season (null for entire series).</param>
    /// <param name="autoWatch">Whether to enable or disable auto-watch.</param>
    /// <returns>The task result contains the number of files marked as wanted, or an error.</returns>
    Task<ErrorOr<int>> SetAutoWatchAsync(string seriesName, string? seasonName, bool autoWatch);

    /// <summary>
    /// Removes auto-watch configuration for a series or season.
    /// Does not change IsWanted status of existing files.
    /// </summary>
    /// <param name="seriesName">The name of the series.</param>
    /// <param name="seasonName">The name of the season (null for entire series).</param>
    /// <returns>True if configuration was removed, false if it didn't exist.</returns>
    Task<ErrorOr<bool>> RemoveAutoWatchAsync(string seriesName, string? seasonName);

    /// <summary>
    /// Retrieves all watch configurations.
    /// </summary>
    /// <returns>The task result contains a list of all watch configurations.</returns>
    Task<List<SeriesWatchConfigDto>> GetAllWatchConfigsAsync();

    /// <summary>
    /// Checks if a series/season should be auto-marked as wanted.
    /// </summary>
    /// <param name="seriesName">The name of the series.</param>
    /// <param name="seasonName">The name of the season.</param>
    /// <returns>True if this series/season is watched.</returns>
    Task<bool> ShouldAutoMarkWantedAsync(string seriesName, string seasonName);

    /// <summary>
    /// Retrieves series groups with statistics and watch status for UI.
    /// </summary>
    /// <returns>The task result contains series groups with season breakdowns and watch flags.</returns>
    Task<List<SeriesGroupDto>> GetSeriesGroupsWithWatchStatusAsync();
}
