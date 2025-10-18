using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Services;

public class ApiUsageService(IApiUsageRepository repository, ISettingsService settingsService) : IApiUsageService
{
    public async Task<bool> CanMakeRequestAsync(string model)
    {
        var rateLimitPerMinute = await GetIntSettingAsync("RateLimitPerMinute");
        var rateLimitPerDay = await GetIntSettingAsync("RateLimitPerDay");

        var todayCount = await repository.GetTodayCountForModelAsync(model);
        var lastMinuteCount = await repository.GetLastMinuteCountForModelAsync(model);

        return todayCount < rateLimitPerDay && lastMinuteCount < rateLimitPerMinute;
    }

    public async Task RecordUsageAsync(ApiUsageDto usage)
    {
        await repository.AddAsync(usage);
    }

    public async Task<List<ApiUsageDto>> GetUsageStatsAsync(DateTime from, DateTime to, string? model = null)
    {
        return await repository.GetByDateRangeAsync(from, to, model);
    }

    private async Task<int> GetIntSettingAsync(string key)
    {
        var value = await settingsService.GetSettingAsync(key);
        return int.TryParse(value, out var result) ? result : throw new ArgumentException($"Invalid value for setting {key}, value: {value}");
    }
}

