using Translarr.Core.Api.Models;
using Translarr.Frontend.HavitWebApp.Auth;

namespace Translarr.Frontend.HavitWebApp.Services;

public class TranslationApiService(AuthenticatedApiClientFactory apiClientFactory)
{
    public async Task<bool> StartTranslationAsync(int batchSize = 100)
    {
        var client = apiClientFactory.CreateClient();
        var response = await client.PostAsync($"/api/translation/translate?batchSize={batchSize}", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> CancelTranslationAsync()
    {
        var client = apiClientFactory.CreateClient();
        var response = await client.PostAsync("/api/translation/cancel", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<TranslationStatus?> GetTranslationStatusAsync()
    {
        var client = apiClientFactory.CreateClient();
        var response = await client.GetAsync("/api/translation/status");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TranslationStatus>();
    }

    public async Task<bool> StartBitmapTranslationAsync(int batchSize = 100)
    {
        var client = apiClientFactory.CreateClient();
        var response = await client.PostAsync($"/api/translation/translate-bitmap?batchSize={batchSize}", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> CancelBitmapTranslationAsync()
    {
        var client = apiClientFactory.CreateClient();
        var response = await client.PostAsync("/api/translation/cancel-bitmap", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<TranslationStatus?> GetBitmapTranslationStatusAsync()
    {
        var client = apiClientFactory.CreateClient();
        var response = await client.GetAsync("/api/translation/bitmap-status");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TranslationStatus>();
    }
}
