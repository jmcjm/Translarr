using System.Net.Http.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;
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

    private static async Task HandleLogin(HttpContext context, IHttpClientFactory factory, IOptions<AuthOptions> authOptions)
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
                SetTokenCookie(context, authOptions.Value, result.Token, persistent: form.ContainsKey("rememberMe"));
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

    private static async Task HandleSetup(HttpContext context, IHttpClientFactory factory, IOptions<AuthOptions> authOptions)
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
                SetTokenCookie(context, authOptions.Value, result.Token, persistent: true);
            }

            context.Response.Redirect("/");
        }
        else
        {
            context.Response.Redirect("/setup?error=failed");
        }
    }

    private static Task HandleLogout(HttpContext context, IOptions<AuthOptions> authOptions)
    {
        context.Response.Cookies.Delete(authOptions.Value.CookieName);
        context.Response.Redirect("/login");
        return Task.CompletedTask;
    }

    private static void SetTokenCookie(HttpContext context, AuthOptions options, string token, bool persistent)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = false,
            Path = "/"
        };

        if (persistent)
        {
            cookieOptions.Expires = DateTimeOffset.UtcNow.AddDays(30);
        }

        context.Response.Cookies.Append(options.CookieName, token, cookieOptions);
    }

    private record TokenResponse(string? Token);
}
