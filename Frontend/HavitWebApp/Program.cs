using Havit.Blazor.Components.Web;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Translarr.Core.Application.Constants;
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
        builder.Services.AddScoped<AuthenticatedApiClientFactory>();
        builder.Services.AddScoped<AuthenticationStateProvider, TranslarrAuthStateProvider>();
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization(options =>
        {
        });
        builder.Services.AddHttpContextAccessor();

        // Data Protection - shared keys with API
        var dpKeysPath = builder.Configuration["DataProtection:KeysPath"] ?? AuthConstants.DefaultDpKeysPath;
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath))
            .SetApplicationName(AuthConstants.DataProtectionAppName);

        // HttpClient for API - cookie is added by AuthenticatedApiClientFactory (scoped, circuit-aware)
        var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https+http://Translarr-Api";
        builder.Services.AddHttpClient("TranslarrApi", client =>
        {
            client.BaseAddress = new Uri(apiBaseUrl);
        });

        // HttpClient without cookie handling - for login/setup/logout proxy endpoints
        builder.Services.AddHttpClient("TranslarrApiDirect", client =>
        {
            client.BaseAddress = new Uri(apiBaseUrl);
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            UseCookies = false
        });

        // API services
        builder.Services.AddScoped<LibraryApiService>();
        builder.Services.AddScoped<TranslationApiService>();
        builder.Services.AddScoped<SettingsApiService>();
        builder.Services.AddScoped<StatsApiService>();
        builder.Services.AddScoped<SeriesWatchApiService>();
        builder.Services.AddScoped<AuthApiService>();

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
                context.Request.Cookies.TryGetValue(AuthConstants.CookieName, out var cookieValue))
            {
                cookieHolder.CookieValue = cookieValue;
            }
            await next();
        });

        app.UseAntiforgery();

        app.MapAccountEndpoints();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}
