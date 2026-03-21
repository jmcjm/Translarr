namespace Translarr.Core.Application.Constants;

public static class AuthConstants
{
    public const string CookieName = ".Translarr.Auth";
    public const int MinPasswordLength = 8;
    public const string DataProtectionAppName = "Translarr";
    public const string DefaultDpKeysPath = "/app/data/dp-keys";

    // JWT
    public const string DefaultJwtSecret = "TranslarrDevSecret_ChangeInProduction_AtLeast32Chars!!";
    public const int JwtExpirationDays = 30;
}
