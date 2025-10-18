using System.Net.Http.Json;
using Translarr.Core.Api.Models;

namespace Translarr.Frontend.WebApp.Services;

public class TranslationApiService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public TranslationApiService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<bool> StartTranslationAsync(int batchSize = 1)
    {
        var client = _httpClientFactory.CreateClient("TranslarrApi");
        var response = await client.PostAsync($"/api/translation/translate?batchSize={batchSize}", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<TranslationStatus?> GetTranslationStatusAsync()
    {
        var client = _httpClientFactory.CreateClient("TranslarrApi");
        var response = await client.GetAsync("/api/translation/status");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TranslationStatus>();
    }
}

