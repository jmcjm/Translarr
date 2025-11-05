using System.Net.Http.Json;
using Translarr.Core.Api.Models;
using Translarr.Core.Application.Models;

namespace Translarr.Frontend.WebApp.Services;

public class LibraryApiService(IHttpClientFactory httpClientFactory)
{
    public async Task<ScanResultDto?> ScanLibraryAsync()
    {
        var client = httpClientFactory.CreateClient("TranslarrApi");
        var response = await client.PostAsync("/api/library/scan", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ScanResultDto>();
    }

    public async Task<PagedResult<SubtitleEntryDto>?> GetEntriesAsync(
        int page = 1, 
        int pageSize = 50,
        bool? isProcessed = null,
        bool? isWanted = null,
        bool? alreadyHas = null,
        string? search = null)
    {
        var client = httpClientFactory.CreateClient("TranslarrApi");
        
        var queryParams = new List<string>
        {
            $"page={page}",
            $"pageSize={pageSize}"
        };

        if (isProcessed.HasValue)
            queryParams.Add($"isProcessed={isProcessed.Value}");
        
        if (isWanted.HasValue)
            queryParams.Add($"isWanted={isWanted.Value}");
        
        if (alreadyHas.HasValue)
            queryParams.Add($"alreadyHas={alreadyHas.Value}");
        
        if (!string.IsNullOrWhiteSpace(search))
            queryParams.Add($"search={Uri.EscapeDataString(search)}");

        var queryString = string.Join("&", queryParams);
        var response = await client.GetAsync($"/api/library/entries?{queryString}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PagedResult<SubtitleEntryDto>>();
    }

    public async Task<SubtitleEntryDto?> GetEntryByIdAsync(int id)
    {
        var client = httpClientFactory.CreateClient("TranslarrApi");
        var response = await client.GetAsync($"/api/library/entries/{id}");
        
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SubtitleEntryDto>();
    }

    public async Task<SubtitleEntryDto?> UpdateWantedStatusAsync(int id, bool isWanted)
    {
        var client = httpClientFactory.CreateClient("TranslarrApi");
        var response = await client.PatchAsJsonAsync($"/api/library/entries/{id}/wanted", 
            new UpdateWantedRequest(isWanted));
        
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SubtitleEntryDto>();
    }
}

