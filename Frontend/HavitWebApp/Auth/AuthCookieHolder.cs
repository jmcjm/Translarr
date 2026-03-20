namespace Translarr.Frontend.HavitWebApp.Auth;

/// <summary>
/// Circuit-scoped service that holds the auth cookie captured during the
/// initial HTTP request. HttpContext is null during SignalR interactions,
/// so we capture the cookie once and reuse it for the circuit lifetime.
/// </summary>
public class AuthCookieHolder
{
    public string? CookieValue { get; set; }
}
