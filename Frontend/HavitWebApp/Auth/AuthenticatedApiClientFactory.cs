using Translarr.Core.Application.Constants;

namespace Translarr.Frontend.HavitWebApp.Auth;

/// <summary>
/// Scoped factory that creates HttpClient instances with the auth cookie attached.
/// Replaces CookieForwardingHandler which couldn't access the circuit-scoped
/// AuthCookieHolder from IHttpClientFactory's internal scope.
/// </summary>
public class AuthenticatedApiClientFactory(IHttpClientFactory inner, AuthCookieHolder cookieHolder)
{
    public HttpClient CreateClient()
    {
        var client = inner.CreateClient("TranslarrApi");
        if (!string.IsNullOrEmpty(cookieHolder.CookieValue))
        {
            client.DefaultRequestHeaders.Add("Cookie",
                $"{AuthConstants.CookieName}={cookieHolder.CookieValue}");
        }
        return client;
    }
}
