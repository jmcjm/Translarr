using System.Net.Http.Json;

namespace Translarr.Frontend.HavitWebApp.Auth;

public static class AccountEndpoints
{
    public static WebApplication MapAccountEndpoints(this WebApplication app)
    {
        app.MapPost("/account/login", HandleLogin).AllowAnonymous();
        app.MapPost("/account/setup", HandleSetup).AllowAnonymous();
        app.MapPost("/account/logout", HandleLogout).AllowAnonymous();
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

        if (response.IsSuccessStatusCode &&
            response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies)
            {
                context.Response.Headers.Append("Set-Cookie", cookie);
            }
            var returnUrl = form["returnUrl"].FirstOrDefault();
            var safeUrl = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//")
                ? returnUrl : "/";
            context.Response.Redirect(safeUrl);
        }
        else
        {
            var statusCode = (int)response.StatusCode;
            var errorParam = statusCode switch
            {
                423 => "locked",
                429 => "ratelimit",
                _ => "invalid"
            };
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

        if (response.IsSuccessStatusCode &&
            response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies)
            {
                context.Response.Headers.Append("Set-Cookie", cookie);
            }
            context.Response.Redirect("/");
        }
        else
        {
            context.Response.Redirect("/setup?error=failed");
        }
    }

    private static async Task HandleLogout(HttpContext context, IHttpClientFactory factory)
    {
        var client = factory.CreateClient("TranslarrApiDirect");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        if (context.Request.Cookies.TryGetValue(AuthConstants.CookieName, out var cookie))
        {
            request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        }
        await client.SendAsync(request);
        context.Response.Cookies.Delete(AuthConstants.CookieName);
        context.Response.Redirect("/login");
    }
}
