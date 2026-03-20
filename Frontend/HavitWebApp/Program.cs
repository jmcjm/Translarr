using Havit.Blazor.Components.Web;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Translarr.Frontend.HavitWebApp.Auth;
using Translarr.Frontend.HavitWebApp.Components;
using Translarr.Frontend.HavitWebApp.Services;
using Translarr.ServiceDefaults;

namespace Translarr.Frontend.HavitWebApp;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddHxServices();
        builder.Services.AddHxMessenger();

        builder.Services.AddScoped<ThemeService>();

        // Auth services
        builder.Services.AddScoped<AuthCookieHolder>();
        builder.Services.AddTransient<CookieForwardingHandler>();
        builder.Services.AddScoped<AuthenticationStateProvider, TranslarrAuthStateProvider>();
        builder.Services.AddAuthorization();
        builder.Services.AddHttpContextAccessor();

        // Data Protection - shared keys with API
        var dpKeysPath = builder.Configuration["DataProtection:KeysPath"] ?? "/app/data/dp-keys";
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath))
            .SetApplicationName("Translarr");

        // HttpClient for API with cookie forwarding (used by all API services and auth state provider)
        var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https+http://Translarr-Api";
        builder.Services.AddHttpClient("TranslarrApi", client =>
        {
            client.BaseAddress = new Uri(apiBaseUrl);
        }).AddHttpMessageHandler<CookieForwardingHandler>();

        // HttpClient without cookie forwarding - for login/setup/logout proxy endpoints
        builder.Services.AddHttpClient("TranslarrApiDirect", client =>
        {
            client.BaseAddress = new Uri(apiBaseUrl);
        });

        // API services
        builder.Services.AddScoped<LibraryApiService>();
        builder.Services.AddScoped<TranslationApiService>();
        builder.Services.AddScoped<SettingsApiService>();
        builder.Services.AddScoped<StatsApiService>();
        builder.Services.AddScoped<SeriesWatchApiService>();

        var app = builder.Build();

        app.MapDefaultEndpoints();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseStaticFiles();

        // Capture auth cookie from browser request into circuit-scoped holder
        app.Use(async (context, next) =>
        {
            var cookieHolder = context.RequestServices.GetService<AuthCookieHolder>();
            if (cookieHolder != null &&
                context.Request.Cookies.TryGetValue(".Translarr.Auth", out var cookieValue))
            {
                cookieHolder.CookieValue = cookieValue;
            }
            await next();
        });

        app.UseAntiforgery();

        // WebApp-side login endpoint: proxies to API, forwards Set-Cookie to browser
        app.MapPost("/account/login", async (HttpContext context, IHttpClientFactory factory) =>
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
        }).AllowAnonymous();

        // WebApp-side setup endpoint: proxies to API, forwards Set-Cookie
        app.MapPost("/account/setup", async (HttpContext context, IHttpClientFactory factory) =>
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
        }).AllowAnonymous();

        // WebApp-side logout endpoint
        app.MapPost("/account/logout", async (HttpContext context, IHttpClientFactory factory) =>
        {
            var client = factory.CreateClient("TranslarrApiDirect");
            if (context.Request.Cookies.TryGetValue(".Translarr.Auth", out var cookie))
            {
                client.DefaultRequestHeaders.Add("Cookie", $".Translarr.Auth={cookie}");
            }
            await client.PostAsync("/api/auth/logout", null);
            context.Response.Cookies.Delete(".Translarr.Auth");
            context.Response.Redirect("/login");
        }).AllowAnonymous();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}
