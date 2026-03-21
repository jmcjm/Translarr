using System.Net.Http.Json;
using Microsoft.AspNetCore.Antiforgery;
using Translarr.Core.Application.Constants;

namespace Translarr.Frontend.HavitWebApp.Auth;

public static class AccountEndpoints
{
    public static WebApplication MapAccountEndpoints(this WebApplication app)
    {
        app.MapPost("/account/login", HandleLogin).AllowAnonymous()
            .WithMetadata(new RequireAntiforgeryTokenAttribute());
        app.MapPost("/account/setup", HandleSetup).AllowAnonymous()
            .WithMetadata(new RequireAntiforgeryTokenAttribute());
        app.MapPost("/account/logout", HandleLogout).AllowAnonymous()
            .WithMetadata(new RequireAntiforgeryTokenAttribute());
        return app;
    }

    private static async Task HandleLogin(HttpContext context, IHttpClientFactory factory)
    {
        var form = await context.Request.ReadFormAsync();
        var client = factory.CreateClient("TranslarrApiDirect");
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = form["username"].ToString(),
            password = form["password"].ToString(),
            rememberMe = form.ContainsKey("rememberMe")
        });

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<TokenResponse>();
            if (result?.Token != null)
            {
                var cookieOptions = new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Secure = false,
                    Path = "/"
                };

                if (form.ContainsKey("rememberMe"))
                {
                    cookieOptions.Expires = DateTimeOffset.UtcNow.AddDays(AuthConstants.JwtExpirationDays);
                }

                context.Response.Cookies.Append(AuthConstants.CookieName, result.Token, cookieOptions);
            }

            var returnUrl = form["returnUrl"].FirstOrDefault();
            var safeUrl = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//")
                ? returnUrl : "/";
            context.Response.Redirect(safeUrl);
        }
        else
        {
            var errorParam = (int)response.StatusCode == 429 ? "ratelimit" : "invalid";
            context.Response.Redirect($"/login?error={errorParam}");
        }
    }

    private static async Task HandleSetup(HttpContext context, IHttpClientFactory factory)
    {
        var form = await context.Request.ReadFormAsync();
        var password = form["password"].ToString();
        var confirmPassword = form["confirmPassword"].ToString();

        if (password != confirmPassword)
        {
            context.Response.Redirect("/setup?error=mismatch");
            return;
        }

        var client = factory.CreateClient("TranslarrApiDirect");
        var response = await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = form["username"].ToString(),
            password
        });

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<TokenResponse>();
            if (result?.Token != null)
            {
                var cookieOptions = new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Secure = false,
                    Path = "/",
                    Expires = DateTimeOffset.UtcNow.AddDays(AuthConstants.JwtExpirationDays)
                };

                context.Response.Cookies.Append(AuthConstants.CookieName, result.Token, cookieOptions);
            }

            context.Response.Redirect("/");
        }
        else
        {
            context.Response.Redirect("/setup?error=failed");
        }
    }

    private static Task HandleLogout(HttpContext context, IHttpClientFactory factory)
    {
        context.Response.Cookies.Delete(AuthConstants.CookieName);
        context.Response.Redirect("/login");
        return Task.CompletedTask;
    }

    private record TokenResponse(string? Token);
}
