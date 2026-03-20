namespace Translarr.Frontend.HavitWebApp.Auth;

/// <summary>
/// DelegatingHandler that attaches the auth cookie to outgoing API requests.
/// Reads from circuit-scoped AuthCookieHolder, NOT from IHttpContextAccessor.
/// Must be registered as Transient (required by IHttpClientFactory).
/// </summary>
public class CookieForwardingHandler(AuthCookieHolder cookieHolder) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(cookieHolder.CookieValue))
        {
            request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookieHolder.CookieValue}");
        }

        return base.SendAsync(request, cancellationToken);
    }
}
