namespace Translarr.Frontend.HavitWebApp.Services;

public class AuthApiService(IHttpClientFactory httpClientFactory)
{
    public async Task<bool> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        var client = httpClientFactory.CreateClient("TranslarrApi");
        var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword,
            newPassword
        });
        return response.IsSuccessStatusCode;
    }
}
