using System.Net.Http.Json;
using Translarr.Core.Api.Models;

namespace Translarr.Frontend.WebApp.Services;

public class TranslationApiService(IHttpClientFactory httpClientFactory)
{
    public async Task<bool> StartTranslationAsync(int batchSize = 100)
    {
        var client = httpClientFactory.CreateClient("TranslarrApi");
        var response = await client.PostAsync($"/api/translation/translate?batchSize={batchSize}", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<TranslationStatus?> GetTranslationStatusAsync()
    {
        var client = httpClientFactory.CreateClient("TranslarrApi");
        var response = await client.GetAsync("/api/translation/status");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TranslationStatus>();
    }
}

