using Microsoft.EntityFrameworkCore;
using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Models;
using Translarr.Core.Infrastructure.Persistence;
using Translarr.Core.Infrastructure.Persistence.Daos;

namespace Translarr.Core.Infrastructure.Repositories;

public class ApiUsageRepository(TranslarrDbContext context) : IApiUsageRepository
{
    public async Task AddAsync(ApiUsageDto usage)
    {
        var dao = MapToDao(usage);
        context.ApiUsage.Add(dao);
        await context.SaveChangesAsync();
    }

    public async Task<List<ApiUsageDto>> GetByDateRangeAsync(DateTime from, DateTime to, string? model = null)
    {
        var query = context.ApiUsage
            .Where(u => u.Date >= from && u.Date <= to)
            .AsNoTracking();

        if (!string.IsNullOrEmpty(model))
        {
            query = query.Where(u => u.Model == model);
        }

        var usages = await query.ToListAsync();
        return usages.Select(MapToDto).ToList();
    }

    public async Task<List<ApiUsageDto>> GetTodayAsync(string? model = null)
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        
        return await GetByDateRangeAsync(today, tomorrow, model);
    }

    public async Task<int> GetTodayCountForModelAsync(string model)
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        
        return await context.ApiUsage
            .Where(u => u.Model == model && u.Date >= today && u.Date < tomorrow)
            .CountAsync();
    }

    public async Task<int> GetLastMinuteCountForModelAsync(string model)
    {
        var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
        
        return await context.ApiUsage
            .Where(u => u.Model == model && u.Date >= oneMinuteAgo)
            .CountAsync();
    }

    private static ApiUsageDto MapToDto(ApiUsageDao dao)
    {
        return new ApiUsageDto
        {
            Id = dao.Id,
            Model = dao.Model,
            Date = dao.Date
        };
    }

    private static ApiUsageDao MapToDao(ApiUsageDto dto)
    {
        return new ApiUsageDao
        {
            Id = dto.Id,
            Model = dto.Model,
            Date = dto.Date
        };
    }
}

