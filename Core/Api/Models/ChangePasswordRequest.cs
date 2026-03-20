namespace Translarr.Core.Api.Models;

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
