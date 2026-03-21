using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Translarr.Core.Application.Constants;

namespace Translarr.Frontend.HavitWebApp.Auth;

public class TranslarrAuthStateProvider(
    AuthenticatedApiClientFactory apiClientFactory,
    AuthCookieHolder cookieHolder,
    IHttpContextAccessor httpContextAccessor,
    ILogger<TranslarrAuthStateProvider> logger) : AuthenticationStateProvider
{
    private static AuthenticationState AnonymousState =>
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    private bool _initialized;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        // On first call (SSR render), HttpContext is available - grab cookie directly
        if (!_initialized)
        {
            _initialized = true;
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext != null &&
                httpContext.Request.Cookies.TryGetValue(AuthConstants.CookieName, out var cookie) &&
                !string.IsNullOrEmpty(cookie))
            {
                cookieHolder.CookieValue = cookie;
                logger.LogInformation("Cookie captured from HttpContext on first call, length={Length}", cookie.Length);
            }
        }

        if (string.IsNullOrEmpty(cookieHolder.CookieValue))
        {
            return AnonymousState;
        }

        try
        {
            var client = apiClientFactory.CreateClient();
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
