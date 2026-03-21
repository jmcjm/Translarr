namespace Translarr.Core.Application.Constants;

public class AuthOptions
{
    public const string SectionName = "Auth";

    public string CookieName { get; set; } = ".Translarr.Auth";
    public int MinPasswordLength { get; set; } = 8;
}
