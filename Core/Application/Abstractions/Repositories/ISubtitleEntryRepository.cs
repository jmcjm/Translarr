using ErrorOr;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Repositories;

public interface ISubtitleEntryRepository
{
    /// <summary>
    /// Retrieves a list of subtitle entries that are unprocessed, wanted and didn't have subs in the chosen language at the time of running library scan,
    /// or those that have flag ForceProcess set to true.
    /// </summary>
    /// <param name="take">The maximum number of subtitle entries to retrieve. Defaults to 100 if not specified.</param>
    /// <returns>The task result contains a list of <see cref="SubtitleEntryDto"/> objects.</returns>
    Task<List<SubtitleEntryDto>> GetUnprocessedWantedAsync(int take = 100);

    /// <summary>
    /// Retrieves a subtitle entry based on the specified file path.
    /// </summary>
    /// <param name="filePath">The file path of the subtitle entry to retrieve.</param>
    /// <returns>The task result contains the <see cref="SubtitleEntryDto"/> matching the file path, or null if not found.</returns>
    Task<SubtitleEntryDto?> GetByFilePathAsync(string filePath);

    /// <summary>
    /// Adds a new subtitle entry to the repository.
    /// </summary>
    /// <param name="entry">The subtitle entry to be added.</param>
    /// <returns>A task that represents the asynchronous add operation.</returns>
    void Add(SubtitleEntryDto entry);

    /// <summary>
    /// Updates the provided subtitle entry in the repository.
    /// </summary>
    /// <param name="entry">The subtitle entry to update.</param>
    /// <returns>The task result contains the updated <see cref="SubtitleEntryDto"/> object.</returns>
    Task<SubtitleEntryDto> UpdateAsync(SubtitleEntryDto entry);

    /// <summary>
    /// Retrieves all subtitle entries from the repository.
    /// </summary>
    /// <returns>The task result contains a list of <see cref="SubtitleEntryDto"/> objects.</returns>
    Task<List<SubtitleEntryDto>> GetAllAsync();

    /// <summary>
    /// Retrieves a subtitle entry identified by the specified ID.
    /// </summary>
    /// <param name="id">The unique identifier of the subtitle entry to retrieve.</param>
    /// <returns>The task result contains the <see cref="SubtitleEntryDto"/> matching the ID, or null if not found.</returns>
    Task<ErrorOr<SubtitleEntryDto>> GetByIdAsync(int id);

    /// <summary>
    /// Retrieves subtitle entries with filtering and pagination support.
    /// </summary>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="isProcessed">Filter by processed status.</param>
    /// <param name="isWanted">Filter by wanted status.</param>
    /// <param name="alreadyHas">Filter by already has subtitles status.</param>
    /// <param name="search">Search term for file name, series, or season.</param>
    /// <returns>The task result contains a <see cref="PagedResult{SubtitleEntryDto}"/> with filtered and paginated entries.</returns>
    Task<PagedResult<SubtitleEntryDto>> GetPagedAsync(
        int page = 1,
        int pageSize = 50,
        bool? isProcessed = null,
        bool? isWanted = null,
        bool? alreadyHas = null,
        string? search = null);

    /// <summary>
    /// Deletes subtitle entries identified by the provided IDs.
    /// </summary>
    /// <param name="ids">Collection of subtitle entry IDs to delete.</param>
    /// <returns>The number of entries removed.</returns>
    Task<int> DeleteByIdsAsync(IEnumerable<int> ids);

    /// <summary>
    /// Bulk updates the IsWanted flag for entries matching series and optional season.
    /// </summary>
    /// <param name="seriesName">The name of the series.</param>
    /// <param name="seasonName">The name of the season (null to update entire series).</param>
    /// <param name="isWanted">The new wanted status.</param>
    /// <returns>The number of entries updated.</returns>
    Task<int> BulkUpdateWantedAsync(string seriesName, string? seasonName, bool isWanted);

    /// <summary>
    /// Retrieves series groups with statistics for UI display.
    /// </summary>
    /// <returns>The task result contains a list of <see cref="SeriesGroupDto"/> with nested season statistics.</returns>
    Task<List<SeriesGroupDto>> GetSeriesGroupsAsync();
}

