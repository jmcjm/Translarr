namespace Translarr.Core.Application.Models;

public class AuthResultDto
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, string[]>? ValidationErrors { get; set; }

    public static AuthResultDto Ok(string token) => new() { Success = true, Token = token };
    public static AuthResultDto Fail(string error) => new() { Success = false, Error = error };
    public static AuthResultDto ValidationFail(Dictionary<string, string[]> errors) => new() { Success = false, ValidationErrors = errors };
}
