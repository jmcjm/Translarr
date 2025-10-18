using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Services;

public interface IApiUsageService
{
    Task<bool> CanMakeRequestAsync(string model);
    Task RecordUsageAsync(ApiUsageDto usage);
    Task<List<ApiUsageDto>> GetUsageStatsAsync(DateTime from, DateTime to, string? model = null);
}