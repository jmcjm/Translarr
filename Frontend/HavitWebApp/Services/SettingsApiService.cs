using Translarr.Core.Api.Models;
using Translarr.Core.Application.Models;
using Translarr.Frontend.HavitWebApp.Auth;

namespace Translarr.Frontend.HavitWebApp.Services;

public class SettingsApiService(AuthenticatedApiClientFactory apiClientFactory)
{
    public async Task<List<AppSettingDto>?> GetAllSettingsAsync()
    {
        var client = apiClientFactory.CreateClient();
        var response = await client.GetAsync("/api/settings");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<AppSettingDto>>();
    }

    public async Task<AppSettingDto?> GetSettingAsync(string key)
    {
        var client = apiClientFactory.CreateClient();
        var response = await client.GetAsync($"/api/settings/{key}");

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AppSettingDto>();
    }

    public async Task<bool> UpdateSettingAsync(string key, string value)
    {
        var client = apiClientFactory.CreateClient();
        var response = await client.PutAsJsonAsync($"/api/settings/{key}",
            new UpdateSettingRequest(value));
        return response.IsSuccessStatusCode;
    }

    public async Task<ApiTestResult?> TestApiConnectionAsync()
    {
        var client = apiClientFactory.CreateClient();
        var response = await client.PostAsync("/api/settings/test-api", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ApiTestResult>();
    }

}
