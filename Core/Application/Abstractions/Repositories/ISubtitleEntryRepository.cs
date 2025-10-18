using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Repositories;

public interface ISubtitleEntryRepository
{
    Task<List<SubtitleEntryDto>> GetUnprocessedWantedAsync(int take = 100);
    Task<SubtitleEntryDto?> GetByFilePathAsync(string filePath);
    Task AddAsync(SubtitleEntryDto entry);
    Task<SubtitleEntryDto> UpdateAsync(SubtitleEntryDto entry);
    Task<List<SubtitleEntryDto>> GetAllAsync();
    Task<SubtitleEntryDto?> GetByIdAsync(int id);
}

