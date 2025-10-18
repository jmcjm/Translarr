using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Repositories;

public interface IApiUsageRepository
{
    Task AddAsync(ApiUsageDto usage);
    Task<List<ApiUsageDto>> GetByDateRangeAsync(DateTime from, DateTime to, string? model = null);
    Task<List<ApiUsageDto>> GetTodayAsync(string? model = null);
    Task<int> GetTodayCountForModelAsync(string model);
    Task<int> GetLastMinuteCountForModelAsync(string model);
}

