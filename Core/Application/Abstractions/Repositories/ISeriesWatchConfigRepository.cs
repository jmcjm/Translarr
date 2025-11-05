using ErrorOr;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Repositories;

public interface ISeriesWatchConfigRepository
{
    /// <summary>
    /// Retrieves all watch configurations.
    /// </summary>
    /// <returns>The task result contains a list of <see cref="SeriesWatchConfigDto"/> objects.</returns>
    Task<List<SeriesWatchConfigDto>> GetAllAsync();

    /// <summary>
    /// Retrieves a watch configuration by series and optional season.
    /// </summary>
    /// <param name="seriesName">The name of the series.</param>
    /// <param name="seasonName">The name of the season (null for series-level config).</param>
    /// <returns>The task result contains the <see cref="SeriesWatchConfigDto"/> or null if not found.</returns>
    Task<SeriesWatchConfigDto?> GetAsync(string seriesName, string? seasonName);

    /// <summary>
    /// Checks if a series/season has auto-watch enabled.
    /// </summary>
    /// <param name="seriesName">The name of the series.</param>
    /// <param name="seasonName">The name of the season.</param>
    /// <returns>True if auto-watch is enabled for this series/season.</returns>
    Task<bool> IsWatchedAsync(string seriesName, string seasonName);

    /// <summary>
    /// Adds a new watch configuration.
    /// </summary>
    /// <param name="config">The watch configuration to add.</param>
    /// <returns>The task result contains the added <see cref="SeriesWatchConfigDto"/> with generated ID.</returns>
    Task<ErrorOr<SeriesWatchConfigDto>> AddAsync(SeriesWatchConfigDto config);

    /// <summary>
    /// Deletes a watch configuration by series and optional season.
    /// </summary>
    /// <param name="seriesName">The name of the series.</param>
    /// <param name="seasonName">The name of the season (null for series-level config).</param>
    /// <returns>The task result contains true if deleted, false if not found.</returns>
    Task<bool> DeleteAsync(string seriesName, string? seasonName);
}
