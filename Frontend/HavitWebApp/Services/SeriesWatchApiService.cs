using Translarr.Core.Api.Models;
using Translarr.Core.Application.Models;
using Translarr.Frontend.HavitWebApp.Auth;

namespace Translarr.Frontend.HavitWebApp.Services;

public class SeriesWatchApiService(AuthenticatedApiClientFactory apiClientFactory)
{
    public async Task<List<SeriesGroupDto>?> GetSeriesGroupsAsync()
    {
        var client = apiClientFactory.CreateClient();
        var response = await client.GetAsync("/api/series/series");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<SeriesGroupDto>>();
    }

    public async Task<List<SeriesWatchConfigDto>?> GetAllWatchConfigsAsync()
    {
        var client = apiClientFactory.CreateClient();
        var response = await client.GetAsync("/api/series/watch-configs");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<SeriesWatchConfigDto>>();
    }

    public async Task<SetAutoWatchResult?> SetAutoWatchAsync(string series, string? season, bool autoWatch)
    {
        var client = apiClientFactory.CreateClient();

        var queryParams = new List<string>
        {
            $"series={Uri.EscapeDataString(series)}",
            $"autoWatch={autoWatch}"
        };

        if (season != null)
            queryParams.Add($"season={Uri.EscapeDataString(season)}");

        var queryString = string.Join("&", queryParams);
        var response = await client.PutAsync($"/api/series/watch-configs?{queryString}", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SetAutoWatchResult>();
    }

    public async Task<bool> RemoveAutoWatchAsync(string series, string? season)
    {
        var client = apiClientFactory.CreateClient();

        var queryParams = new List<string>
        {
            $"series={Uri.EscapeDataString(series)}"
        };

        if (season != null)
            queryParams.Add($"season={Uri.EscapeDataString(season)}");

        var queryString = string.Join("&", queryParams);
        var response = await client.DeleteAsync($"/api/series/watch-configs?{queryString}");

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return false;

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<bool>();
        return result;
    }

    public async Task<int> BulkSetWantedAsync(string series, string? season, bool wanted, string? library = null)
    {
        var client = apiClientFactory.CreateClient();

        var queryParams = new List<string>
        {
            $"series={Uri.EscapeDataString(series)}",
            $"wanted={wanted}"
        };

        if (season != null)
            queryParams.Add($"season={Uri.EscapeDataString(season)}");

        if (library != null)
            queryParams.Add($"library={Uri.EscapeDataString(library)}");

        var queryString = string.Join("&", queryParams);
        var response = await client.PutAsync($"/api/library/bulk/wanted?{queryString}", null);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<BulkUpdateResult>();
        return result?.UpdatedCount ?? 0;
    }
}
