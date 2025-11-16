using ErrorOr;
using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Services;

public class LibraryService(ISubtitleEntryRepository repository, IUnitOfWork unitOfWork) : ILibraryService
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
    public async Task<ErrorOr<int>> BulkSetWantedAsync(string seriesName, string? seasonName, bool isWanted)
    {
        if (string.IsNullOrWhiteSpace(seriesName))
            return Error.Validation("LibraryService.BulkSetWantedAsync", "Series name cannot be empty.");

        var updatedCount = await repository.BulkUpdateWantedAsync(seriesName, seasonName, isWanted);
        return updatedCount;
    }
}