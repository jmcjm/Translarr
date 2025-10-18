using Microsoft.EntityFrameworkCore;
using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Models;
using Translarr.Core.Infrastructure.Persistence;
using Translarr.Core.Infrastructure.Persistence.Daos;

namespace Translarr.Core.Infrastructure.Repositories;

public class SubtitleEntryRepository(TranslarrDbContext context) : ISubtitleEntryRepository
{
    public async Task<List<SubtitleEntryDto>> GetUnprocessedWantedAsync(int take = 100)
    {
        var entries = await context.SubtitleEntries
            .Where(e => !e.IsProcessed && e.IsWanted && !e.AlreadyHas)
            .Take(take)
            .ToListAsync();

        return entries.Select(MapToDto).ToList();
    }

    public async Task<SubtitleEntryDto?> GetByFilePathAsync(string filePath)
    {
        var entry = await context.SubtitleEntries
            .FirstOrDefaultAsync(e => e.FilePath == filePath);

        return entry == null ? null : MapToDto(entry);
    }

    public async Task AddAsync(SubtitleEntryDto entry)
    {
        var dao = MapToDao(entry);
        context.SubtitleEntries.Add(dao);
        await context.SaveChangesAsync();
        entry.Id = dao.Id;
    }

    public async Task<SubtitleEntryDto> UpdateAsync(SubtitleEntryDto entry)
    {
        var dao = await context.SubtitleEntries.FindAsync(entry.Id);
        
        if (dao == null)
        {
            throw new InvalidOperationException($"SubtitleEntry with Id {entry.Id} not found");
        }

        dao.Series = entry.Series;
        dao.Season = entry.Season;
        dao.FileName = entry.FileName;
        dao.FilePath = entry.FilePath;
        dao.IsProcessed = entry.IsProcessed;
        dao.IsWanted = entry.IsWanted;
        dao.AlreadyHas = entry.AlreadyHas;
        dao.LastScanned = entry.LastScanned;
        dao.ProcessedAt = entry.ProcessedAt;
        dao.ErrorMessage = entry.ErrorMessage;

        await context.SaveChangesAsync();
        return MapToDto(dao);
    }

    public async Task<List<SubtitleEntryDto>> GetAllAsync()
    {
        var entries = await context.SubtitleEntries.ToListAsync();
        return entries.Select(MapToDto).ToList();
    }

    public async Task<SubtitleEntryDto?> GetByIdAsync(int id)
    {
        var entry = await context.SubtitleEntries.FindAsync(id);
        return entry == null ? null : MapToDto(entry);
    }

    private static SubtitleEntryDto MapToDto(SubtitleEntryDao dao)
    {
        return new SubtitleEntryDto
        {
            Id = dao.Id,
            Series = dao.Series,
            Season = dao.Season,
            FileName = dao.FileName,
            FilePath = dao.FilePath,
            IsProcessed = dao.IsProcessed,
            IsWanted = dao.IsWanted,
            AlreadyHas = dao.AlreadyHas,
            LastScanned = dao.LastScanned,
            ProcessedAt = dao.ProcessedAt,
            ErrorMessage = dao.ErrorMessage
        };
    }

    private static SubtitleEntryDao MapToDao(SubtitleEntryDto dto)
    {
        return new SubtitleEntryDao
        {
            Id = dto.Id,
            Series = dto.Series,
            Season = dto.Season,
            FileName = dto.FileName,
            FilePath = dto.FilePath,
            IsProcessed = dto.IsProcessed,
            IsWanted = dto.IsWanted,
            AlreadyHas = dto.AlreadyHas,
            LastScanned = dto.LastScanned,
            ProcessedAt = dto.ProcessedAt,
            ErrorMessage = dto.ErrorMessage
        };
    }
}

