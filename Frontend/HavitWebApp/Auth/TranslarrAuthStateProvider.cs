using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace Translarr.Frontend.HavitWebApp.Auth;

public class TranslarrAuthStateProvider(
    IHttpClientFactory httpClientFactory,
    AuthCookieHolder cookieHolder,
    ILogger<TranslarrAuthStateProvider> logger) : AuthenticationStateProvider
{
    private static readonly AuthenticationState AnonymousState =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (string.IsNullOrEmpty(cookieHolder.CookieValue))
        {
            return AnonymousState;
        }

        try
        {
            var client = httpClientFactory.CreateClient("TranslarrApi");
            var response = await client.GetAsync("/api/auth/me");

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
