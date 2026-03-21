namespace Translarr.Core.Api.Models;

public record LoginRequest(string Username, string Password, bool RememberMe = false);
