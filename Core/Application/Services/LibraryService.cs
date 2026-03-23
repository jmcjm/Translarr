using ErrorOr;
using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Helpers;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Services;

public class LibraryService(
    ISubtitleEntryRepository repository,
    IUnitOfWork unitOfWork,
    ISeriesWatchService seriesWatchService) : ILibraryService
{
    /// <inheritdoc />
    public async Task<ErrorOr<SubtitleEntryDto>> GetEntryById(int id)
    {
        var entryResult = await repository.GetByIdAsync(id);

        if (entryResult.IsError)
            return entryResult.Errors;
        
        return entryResult.Value;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<SubtitleEntryDto>> SetWantedStatusAsync(int id, bool wantedStatus)
    {
        var entryResult = await repository.GetByIdAsync(id);

        if (entryResult.IsError)
            return entryResult.Errors;

        entryResult.Value.IsWanted = wantedStatus;

        await repository.UpdateAsync(entryResult.Value);
        await unitOfWork.SaveChangesAsync();

        return entryResult.Value;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<SubtitleEntryDto>> SetForceProcessStatusAsync(int id, bool forceProcess)
    {
        var entryResult = await repository.GetByIdAsync(id);

        if (entryResult.IsError)
            return entryResult.Errors;

        entryResult.Value.ForceProcess = forceProcess;

        if (forceProcess)
        {
            entryResult.Value.IsProcessed = false;
            entryResult.Value.ErrorMessage = null;
        }

        await repository.UpdateAsync(entryResult.Value);
        await unitOfWork.SaveChangesAsync();

        return entryResult.Value;
    }

    /// <inheritdoc />
    public async Task<PagedResult<SubtitleEntryDto>> GetEntriesAsync(
        int page = 1,
        int pageSize = 50,
        bool? isProcessed = null,
        bool? isWanted = null,
        bool? alreadyHas = null,
        string? search = null)
    {
        // Walidacja parametrów
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 1000) pageSize = 50;
        
        return await repository.GetPagedAsync(page, pageSize, isProcessed, isWanted, alreadyHas, search);
    }

    /// <inheritdoc />
    public async Task<ErrorOr<int>> BulkSetWantedAsync(string seriesName, string? seasonName, bool isWanted, string? library = null)
    {
        if (string.IsNullOrWhiteSpace(seriesName))
            return Error.Validation("LibraryService.BulkSetWantedAsync", "Series name cannot be empty.");

        var updatedCount = await repository.BulkUpdateWantedAsync(seriesName, seasonName, isWanted, library);
        return updatedCount;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<List<string>>> GetLibrariesAsync()
    {
        var libraries = await repository.GetDistinctLibrariesAsync();
        return libraries;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<SeriesDetailDto>> GetSeriesDetailAsync(string library, string series)
    {
        if (string.IsNullOrWhiteSpace(library))
            return Error.Validation("LibraryService.GetSeriesDetailAsync", "Library name is required.");
        if (string.IsNullOrWhiteSpace(series))
            return Error.Validation("LibraryService.GetSeriesDetailAsync", "Series name is required.");

        var entries = await repository.GetEntriesByLibraryAndSeriesAsync(library, series);
        if (entries.Count == 0)
            return Error.NotFound("LibraryService.GetSeriesDetailAsync", "Series not found in library.");

        var isSeriesWatched = await seriesWatchService.ShouldAutoMarkWantedAsync(series, "");

        var seasons = new List<SeasonDetailDto>();
        foreach (var seasonGroup in entries.GroupBy(e => e.Season)
            .OrderBy(g => NaturalSort.Key(g.Key)))
        {
            var isSeasonWatched = isSeriesWatched ||
                await seriesWatchService.ShouldAutoMarkWantedAsync(series, seasonGroup.Key);

            seasons.Add(new SeasonDetailDto
            {
                SeasonName = seasonGroup.Key,
                TotalFiles = seasonGroup.Count(),
                WantedFiles = seasonGroup.Count(e => e.IsWanted),
                ProcessedFiles = seasonGroup.Count(e => e.IsProcessed),
                IsWatched = isSeasonWatched,
                Entries = seasonGroup.OrderBy(e => e.FileName).ToList()
            });
        }

        return new SeriesDetailDto
        {
            SeriesName = series,
            IsWatched = isSeriesWatched,
            Seasons = seasons
        };
    }
}