using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace Translarr.Frontend.HavitWebApp.Auth;

public class TranslarrAuthStateProvider(
    AuthenticatedApiClientFactory apiClientFactory,
    AuthCookieHolder cookieHolder,
    ILogger<TranslarrAuthStateProvider> logger) : AuthenticationStateProvider
{
    private static AuthenticationState AnonymousState =>
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        logger.LogInformation("GetAuthenticationStateAsync called, cookieValue={HasCookie}",
            !string.IsNullOrEmpty(cookieHolder.CookieValue));

        if (string.IsNullOrEmpty(cookieHolder.CookieValue))
        {
            logger.LogInformation("No cookie in holder, returning anonymous");
            return AnonymousState;
        }

        try
        {
            var client = apiClientFactory.CreateClient();
            var response = await client.GetAsync("/api/auth/me");

            logger.LogInformation("API /auth/me returned {StatusCode}", (int)response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                return AnonymousState;
            }

            var json = await response.Content.ReadFromJsonAsync<MeResponse>();
            if (json is not { IsAuthenticated: true })
            {
                return AnonymousState;
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, json.Username ?? "admin")],
                authenticationType: "Translarr");

            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check auth state via API");
            return AnonymousState;
        }
    }

    private record MeResponse(string? Username, bool IsAuthenticated);
}
