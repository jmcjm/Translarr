namespace Translarr.Core.Api.Models;

public class ApiTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Response { get; set; }
}

