using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Services;

public interface IAuthService
{
    Task<bool> IsSetupNeededAsync();
    Task<AuthResultDto> SetupAsync(string username, string password);
    Task<AuthResultDto> LoginAsync(string username, string password);
    Task<AuthResultDto> ChangePasswordAsync(string userId, string currentPassword, string newPassword);
}
