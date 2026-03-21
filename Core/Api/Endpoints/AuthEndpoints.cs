using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Translarr.Core.Api.Models;
using Translarr.Core.Application.Abstractions.Services;

namespace Translarr.Core.Api.Endpoints;

public static class AuthEndpoints
{
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

    private static async Task<IResult> GetSetupStatus(IAuthService authService)
    {
        var needsSetup = await authService.IsSetupNeededAsync();
        return Results.Ok(new { needsSetup });
    }

    private static async Task<IResult> Setup(
        [FromBody] SetupRequest request,
        IAuthService authService)
    {
        var result = await authService.SetupAsync(request.Username, request.Password);
        return ToResult(result);
    }

    private static async Task<IResult> Login(
        [FromBody] LoginRequest request,
        IAuthService authService)
    {
        var result = await authService.LoginAsync(request.Username, request.Password);
        return result.Success
            ? Results.Ok(new { result.Token })
            : Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: result.Error);
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
        IAuthService authService,
        ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "User not found");
        }

        var result = await authService.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword);
        return ToResult(result);
    }

    private static IResult ToResult(Application.Models.AuthResultDto result)
    {
        if (result.Success)
            return Results.Ok(new { result.Token });

        if (result.ValidationErrors != null)
            return Results.ValidationProblem(result.ValidationErrors);

        return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: result.Error);
    }
}
