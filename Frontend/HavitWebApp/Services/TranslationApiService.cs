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
}
