using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Constants;
using Translarr.Core.Application.Models;

namespace Translarr.Core.Infrastructure.Services;

public class AuthService(
    UserManager<IdentityUser> userManager,
    SignInManager<IdentityUser> signInManager,
    IOptions<JwtOptions> jwtOptions) : IAuthService
{
    private static readonly SemaphoreSlim SetupLock = new(1, 1);

    public async Task<bool> IsSetupNeededAsync()
    {
        return !await userManager.Users.AnyAsync();
    }

    public async Task<AuthResultDto> SetupAsync(string username, string password)
    {
        if (!await SetupLock.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            return AuthResultDto.Fail("Setup already in progress");
        }

        try
        {
            if (await userManager.Users.AnyAsync())
            {
                return AuthResultDto.Fail("Setup not available");
            }

            var user = new IdentityUser { UserName = username };
            var result = await userManager.CreateAsync(user, password);

            if (!result.Succeeded)
            {
                return AuthResultDto.ValidationFail(
                    result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
            }

            if (await userManager.Users.CountAsync() > 1)
            {
                await userManager.DeleteAsync(user);
                return AuthResultDto.Fail("Race condition detected");
            }

            return AuthResultDto.Ok(GenerateJwtToken(user));
        }
        finally
        {
            SetupLock.Release();
        }
    }

    public async Task<AuthResultDto> LoginAsync(string username, string password)
    {
        var user = await userManager.FindByNameAsync(username);
        var result = await signInManager.CheckPasswordSignInAsync(
            user ?? new IdentityUser(),
            password,
            lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            return AuthResultDto.Fail("Invalid credentials");
        }

        return AuthResultDto.Ok(GenerateJwtToken(user!));
    }

    public async Task<AuthResultDto> ChangePasswordAsync(string userId, string currentPassword, string newPassword)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return AuthResultDto.Fail("User not found");
        }

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);

        if (!result.Succeeded)
        {
            return AuthResultDto.ValidationFail(
                result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
        }

        return AuthResultDto.Ok(GenerateJwtToken(user));
    }

    private string GenerateJwtToken(IdentityUser user)
    {
        var options = jwtOptions.Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.UserName ?? "admin")
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddDays(options.ExpirationDays),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
