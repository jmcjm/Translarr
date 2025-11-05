using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Models;
using Translarr.Core.Infrastructure.Persistence;
using Translarr.Core.Infrastructure.Persistence.Daos;

namespace Translarr.Core.Infrastructure.Repositories;

public class SubtitleEntryRepository(TranslarrDbContext context) : ISubtitleEntryRepository
{
    /// <inheritdoc />
    public async Task<List<SubtitleEntryDto>> GetUnprocessedWantedAsync(int take = 100)
    {
        var entries = await context.SubtitleEntries 
            .Where(e => (!e.IsProcessed && e.IsWanted && !e.AlreadyHad) || e.ForceProcess == true)
            .Take(take)
            .AsNoTracking()
            .ToListAsync();

        return entries.Select(MapToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<SubtitleEntryDto?> GetByFilePathAsync(string filePath)
    {
        var entry = await context.SubtitleEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.FilePath == filePath);

        return entry == null ? null : MapToDto(entry);
    }

    /// <inheritdoc />
    public async Task AddAsync(SubtitleEntryDto entry)
    {
        var dao = MapToDao(entry);
        context.SubtitleEntries.Add(dao);
        await context.SaveChangesAsync();
        entry.Id = dao.Id;
    }

    /// <inheritdoc />
    public async Task<SubtitleEntryDto> UpdateAsync(SubtitleEntryDto entry)
    {
        var dao = await context.SubtitleEntries
            .FirstOrDefaultAsync(x => x.Id == entry.Id);
        
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
        dao.AlreadyHad = entry.AlreadyHad;
        dao.LastScanned = entry.LastScanned;
        dao.ProcessedAt = entry.ProcessedAt;
        dao.ErrorMessage = entry.ErrorMessage;

        await context.SaveChangesAsync();
        return MapToDto(dao);
    }

    /// <inheritdoc />
    public async Task<List<SubtitleEntryDto>> GetAllAsync()
    {
        var entries = await context.SubtitleEntries
            .AsNoTracking()
            .ToListAsync();
        
        return entries.Select(MapToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<ErrorOr<SubtitleEntryDto>> GetByIdAsync(int id)
    {
        var entry = await context.SubtitleEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
        
        if (entry == null)
            return Error.NotFound("SubtitleEntryRepository.GetByIdAsync", $"SubtitleEntry with Id {id} not found");
        
        return MapToDto(entry);
    }

    /// <inheritdoc />
    public async Task<PagedResult<SubtitleEntryDto>> GetPagedAsync(
        int page = 1,
        int pageSize = 50,
        bool? isProcessed = null,
        bool? isWanted = null,
        bool? alreadyHas = null,
        string? search = null)
    {
        var query = context.SubtitleEntries.AsNoTracking();

        // Apply filters
        if (isProcessed.HasValue)
            query = query.Where(e => e.IsProcessed == isProcessed.Value);

        if (isWanted.HasValue)
            query = query.Where(e => e.IsWanted == isWanted.Value);

        if (alreadyHas.HasValue)
            query = query.Where(e => e.AlreadyHad == alreadyHas.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Sanityzacja wyszukiwania - ograniczenie długości i usunięcie niebezpiecznych znaków
            var sanitizedSearch = search.Trim();
            if (sanitizedSearch.Length > 100) // Ograniczenie długości
                sanitizedSearch = sanitizedSearch.Substring(0, 100);
            
            var searchPattern = $"%{sanitizedSearch}%";
            query = query.Where(e =>
                EF.Functions.Like(e.FileName, searchPattern) ||
                EF.Functions.Like(e.Series, searchPattern) ||
                EF.Functions.Like(e.Season, searchPattern));
        }

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply pagination
        var items = await query
            .OrderBy(e => e.Series)
            .ThenBy(e => e.Season)
            .ThenBy(e => e.FileName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => MapToDto(e))
            .ToListAsync();

        return new PagedResult<SubtitleEntryDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    /// <inheritdoc />
    public async Task<int> DeleteByIdsAsync(IEnumerable<int> ids)
    {
        var idList = ids
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (idList.Count == 0)
            return 0;

        var entries = await context.SubtitleEntries
            .Where(e => idList.Contains(e.Id))
            .ToListAsync();

        if (entries.Count == 0)
            return 0;

        context.SubtitleEntries.RemoveRange(entries);
        await context.SaveChangesAsync();

        return entries.Count;
    }

    /// <inheritdoc />
    public async Task<int> BulkUpdateWantedAsync(string seriesName, string? seasonName, bool isWanted)
    {
        var query = context.SubtitleEntries.Where(e => e.Series == seriesName);

        if (seasonName != null)
            query = query.Where(e => e.Season == seasonName);

        return await query.ExecuteUpdateAsync(setters =>
            setters.SetProperty(e => e.IsWanted, isWanted));
    }

    /// <inheritdoc />
    public async Task<List<SeriesGroupDto>> GetSeriesGroupsAsync()
    {
        // Group by series and season, calculate stats
        var groupedData = await context.SubtitleEntries
            .AsNoTracking()
            .GroupBy(e => new { e.Series, e.Season })
            .Select(g => new
            {
                g.Key.Series,
                g.Key.Season,
                TotalFiles = g.Count(),
                WantedFiles = g.Count(e => e.IsWanted),
                ProcessedFiles = g.Count(e => e.IsProcessed)
            })
            .ToListAsync();

        // Group by series
        var seriesGroups = groupedData
            .GroupBy(g => g.Series)
            .Select(sg => new SeriesGroupDto
            {
                SeriesName = sg.Key,
                TotalFiles = sg.Sum(s => s.TotalFiles),
                WantedFiles = sg.Sum(s => s.WantedFiles),
                ProcessedFiles = sg.Sum(s => s.ProcessedFiles),
                Seasons = sg.Select(s => new SeasonGroupDto
                {
                    SeasonName = s.Season,
                    TotalFiles = s.TotalFiles,
                    WantedFiles = s.WantedFiles,
                    ProcessedFiles = s.ProcessedFiles
                }).ToList()
            })
            .OrderBy(s => s.SeriesName)
            .ToList();

        return seriesGroups;
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
            AlreadyHad = dao.AlreadyHad,
            ForceProcess = dao.ForceProcess,
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
            AlreadyHad = dto.AlreadyHad,
            ForceProcess = dto.ForceProcess,
            LastScanned = dto.LastScanned,
            ProcessedAt = dto.ProcessedAt,
            ErrorMessage = dto.ErrorMessage
        };
    }
}

