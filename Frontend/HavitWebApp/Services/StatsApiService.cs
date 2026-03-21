using Translarr.Core.Api.Models;
using Translarr.Core.Application.Models;
using Translarr.Frontend.HavitWebApp.Auth;

namespace Translarr.Frontend.HavitWebApp.Services;

public class StatsApiService(AuthenticatedApiClientFactory apiClientFactory)
{
    public async Task<List<ApiUsageDto>?> GetApiUsageStatsAsync(
        DateTime? from = null,
        DateTime? to = null,
        string? model = null)
    {
        var client = apiClientFactory.CreateClient();

        var queryParams = new List<string>();

        if (from.HasValue)
            queryParams.Add($"from={from.Value:O}");

        if (to.HasValue)
            queryParams.Add($"to={to.Value:O}");

        if (!string.IsNullOrWhiteSpace(model))
            queryParams.Add($"model={Uri.EscapeDataString(model)}");

        var queryString = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
        var response = await client.GetAsync($"/api/stats/api-usage{queryString}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ApiUsageDto>>();
    }

    public async Task<LibraryStats?> GetLibraryStatsAsync()
    {
        var client = apiClientFactory.CreateClient();
        var response = await client.GetAsync("/api/stats/library-stats");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LibraryStats>();
    }
}
