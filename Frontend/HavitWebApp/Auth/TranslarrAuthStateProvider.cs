using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Translarr.Core.Application.Constants;

namespace Translarr.Frontend.HavitWebApp.Auth;

public class TranslarrAuthStateProvider(
    AuthCookieHolder cookieHolder,
    IHttpContextAccessor httpContextAccessor,
    ILogger<TranslarrAuthStateProvider> logger) : AuthenticationStateProvider
{
    private static AuthenticationState AnonymousState =>
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    private bool _initialized;

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (!_initialized)
        {
            _initialized = true;
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext != null &&
                httpContext.Request.Cookies.TryGetValue(AuthConstants.CookieName, out var token) &&
                !string.IsNullOrEmpty(token))
            {
                cookieHolder.CookieValue = token;
            }
        }

        if (string.IsNullOrEmpty(cookieHolder.CookieValue))
        {
            return Task.FromResult(AnonymousState);
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(cookieHolder.CookieValue);

            if (jwt.ValidTo < DateTime.UtcNow)
            {
                cookieHolder.CookieValue = null;
                return Task.FromResult(AnonymousState);
            }

            var identity = new ClaimsIdentity(jwt.Claims, authenticationType: "jwt");
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to decode JWT");
            cookieHolder.CookieValue = null;
            return Task.FromResult(AnonymousState);
        }
    }
}
