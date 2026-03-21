namespace Translarr.Frontend.HavitWebApp.Auth;

/// <summary>
/// Circuit-scoped service holding the JWT token.
/// Populated from cookie on circuit start, used by AuthenticatedApiClientFactory.
/// </summary>
public class AuthCookieHolder
{
    public string? CookieValue { get; set; }
}
