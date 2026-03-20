using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
            .AllowAnonymous();

        group.MapPost("/login", Login)
            .WithName("Login")
            .AllowAnonymous()
            .RequireRateLimiting("login");

        group.MapPost("/logout", Logout)
            .WithName("Logout")
            .RequireAuthorization();

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
        SignInManager<IdentityUser> signInManager)
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
                return Results.NotFound();
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

            await signInManager.SignInAsync(user, isPersistent: true);
            return Results.Ok(new { message = "Setup complete" });
        }
        finally
        {
            SetupLock.Release();
        }
    }

    private static async Task<IResult> Login(
        [FromBody] LoginRequest request,
        SignInManager<IdentityUser> signInManager)
    {
        var result = await signInManager.PasswordSignInAsync(
            request.Username,
            request.Password,
            isPersistent: request.RememberMe,
            lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            if (result.IsLockedOut)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status423Locked,
                    title: "Account locked",
                    detail: "Too many failed attempts. Please try again later.");
            }

            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid credentials");
        }

        return Results.Ok(new { message = "Login successful" });
    }

    private static async Task<IResult> Logout(SignInManager<IdentityUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.Ok(new { message = "Logged out" });
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
}

public record SetupRequest(string Username, string Password);
public record LoginRequest(string Username, string Password, bool RememberMe = false);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
