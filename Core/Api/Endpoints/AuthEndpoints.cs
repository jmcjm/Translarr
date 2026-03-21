using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Translarr.Core.Api.Models;
using Translarr.Core.Application.Constants;

namespace Translarr.Core.Api.Endpoints;

public static class AuthEndpoints
{
    private static readonly SemaphoreSlim SetupLock = new(1, 1);

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/setup/status", GetSetupStatus)
            .WithName("GetSetupStatus")
            .AllowAnonymous();

        group.MapPost("/setup", Setup)
            .WithName("Setup")
            .AllowAnonymous()
            .RequireRateLimiting("login");

        group.MapPost("/login", Login)
            .WithName("Login")
            .AllowAnonymous()
            .RequireRateLimiting("login");

        group.MapGet("/me", GetCurrentUser)
            .WithName("GetCurrentUser")
            .RequireAuthorization();

        group.MapPost("/change-password", ChangePassword)
            .WithName("ChangePassword")
            .RequireAuthorization();

        return group;
    }

    private static async Task<IResult> GetSetupStatus(UserManager<IdentityUser> userManager)
    {
        var hasUsers = await userManager.Users.AnyAsync();
        return Results.Ok(new { needsSetup = !hasUsers });
    }

    private static async Task<IResult> Setup(
        [FromBody] SetupRequest request,
        UserManager<IdentityUser> userManager,
        IOptions<JwtOptions> jwtOptions)
    {
        if (!await SetupLock.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            return Results.Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Setup already in progress"
            });
        }

        try
        {
            if (await userManager.Users.AnyAsync())
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Setup not available");
            }

            var user = new IdentityUser { UserName = request.Username };
            var result = await userManager.CreateAsync(user, request.Password);

            if (!result.Succeeded)
            {
                return Results.ValidationProblem(
                    result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
            }

            // Verify no race condition
            if (await userManager.Users.CountAsync() > 1)
            {
                await userManager.DeleteAsync(user);
                return Results.Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Race condition detected"
                });
            }

            var token = GenerateJwtToken(user, jwtOptions.Value);
            return Results.Ok(new { token });
        }
        finally
        {
            SetupLock.Release();
        }
    }

    private static async Task<IResult> Login(
        [FromBody] LoginRequest request,
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        IOptions<JwtOptions> jwtOptions)
    {
        var user = await userManager.FindByNameAsync(request.Username);
        var result = await signInManager.CheckPasswordSignInAsync(
            user ?? new IdentityUser(),
            request.Password,
            lockoutOnFailure: true);

        if (result.IsLockedOut || !result.Succeeded)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid credentials");
        }

        var token = GenerateJwtToken(user!, jwtOptions.Value);
        return Results.Ok(new { token });
    }



    private static IResult GetCurrentUser(ClaimsPrincipal user)
    {
        return Results.Ok(new
        {
            username = user.Identity?.Name,
            isAuthenticated = user.Identity?.IsAuthenticated ?? false
        });
    }

    private static async Task<IResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        UserManager<IdentityUser> userManager,
        ClaimsPrincipal user)
    {
        var identityUser = await userManager.GetUserAsync(user);
        if (identityUser == null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "User not found");
        }

        var result = await userManager.ChangePasswordAsync(identityUser, request.CurrentPassword, request.NewPassword);

        if (!result.Succeeded)
        {
            return Results.ValidationProblem(
                result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
        }

        return Results.Ok(new { message = "Password changed successfully" });
    }

    private static string GenerateJwtToken(IdentityUser user, JwtOptions options)
    {
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
