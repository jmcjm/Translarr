namespace Translarr.Core.Application.Constants;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public int ExpirationDays { get; set; } = 30;
}
