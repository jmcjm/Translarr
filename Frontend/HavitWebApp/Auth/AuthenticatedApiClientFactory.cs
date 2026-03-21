namespace Translarr.Frontend.HavitWebApp.Auth;

/// <summary>
/// Scoped factory that creates HttpClient instances with the JWT Bearer token attached.
/// </summary>
public class AuthenticatedApiClientFactory(IHttpClientFactory inner, AuthCookieHolder cookieHolder)
{
    public HttpClient CreateClient()
    {
        var client = inner.CreateClient("TranslarrApi");
        if (!string.IsNullOrEmpty(cookieHolder.CookieValue))
        {
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cookieHolder.CookieValue);
        }
        return client;
    }
}
