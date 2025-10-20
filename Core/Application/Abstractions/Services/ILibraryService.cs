using ErrorOr;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Services;

public interface ILibraryService
{
    /// <summary>
    /// Retrieves a subtitle entry by its unique identifier.
    /// </summary>
    /// <returns>
    /// A <see cref="Task{TResult}"/> representing the asynchronous operation,
    /// containing an <see cref="ErrorOr{T}"/> with a <see cref="SubtitleEntryDto"/> if found,
    /// or an error if the operation fails or the entry does not exist.
    /// </returns>
    Task<ErrorOr<SubtitleEntryDto>> GetEntryById(int id);

    /// <summary>
    /// Toggles the "wanted" status of a media item with the specified ID.
    /// If the item is currently marked as wanted, it will be set to unwated, and vice versa.
    /// </summary>
    /// <param name="id">ID of the media entry whose wanted status should be toggled.</param>
    /// <param name="wantedStatus">New status</param>
    /// <returns>
    /// A <see cref="Task{TResult}"/> representing the asynchronous operation, 
    /// containing an <see cref="ErrorOr{T}"/> with a <see cref="Success"/> result if the operation succeeds,
    /// or an error if it fails.
    /// </returns>
    Task<ErrorOr<SubtitleEntryDto>> SetWantedStatusAsync(int id, bool wantedStatus);

    /// <summary>
    /// Toggles the "force process" status of a media item with the specified ID.
    /// If the item is currently not marked for force processing, it will be set to force process, and vice versa.
    /// </summary>
    /// <param name="id">ID of the media entry whose force process status should be toggled.</param>
    /// <param name="forceProcess"></param>
    /// <returns>
    /// A <see cref="Task{TResult}"/> representing the asynchronous operation,
    /// containing an <see cref="ErrorOr{T}"/> with a <see cref="Success"/> result if the operation succeeds,
    /// or an error if it fails.
    /// </returns>
    Task<ErrorOr<SubtitleEntryDto>> SetForceProcessStatusAsync(int id, bool forceProcess);

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
    Task<PagedResult<SubtitleEntryDto>> GetEntriesAsync(
        int page = 1,
        int pageSize = 50,
        bool? isProcessed = null,
        bool? isWanted = null,
        bool? alreadyHas = null,
        string? search = null);
}