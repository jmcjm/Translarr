using Translarr.Frontend.HavitWebApp.Auth;

namespace Translarr.Frontend.HavitWebApp.Services;

public class AuthApiService(AuthenticatedApiClientFactory apiClientFactory)
{
    public async Task<bool> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        var client = apiClientFactory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword,
            newPassword
        });
        return response.IsSuccessStatusCode;
    }
}
