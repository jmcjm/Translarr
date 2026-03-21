using Havit.Blazor.Components.Web;
using Microsoft.AspNetCore.Components.Authorization;
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

        // Auth
        builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
        builder.Services.AddScoped<AuthCookieHolder>();
        builder.Services.AddScoped<AuthenticatedApiClientFactory>();
        builder.Services.AddScoped<AuthenticationStateProvider, TranslarrAuthStateProvider>();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddHttpContextAccessor();

        // HttpClient for API - Bearer token added by AuthenticatedApiClientFactory (scoped, circuit-aware)
        var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https+http://Translarr-Api";
        builder.Services.AddHttpClient("TranslarrApi", client =>
        {
            client.BaseAddress = new Uri(apiBaseUrl);
        });

        // HttpClient for login/setup/logout proxy endpoints (no auth needed)
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
        builder.Services.AddScoped<AuthApiService>();

        var app = builder.Build();

        app.MapDefaultEndpoints();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseStaticFiles();

        app.UseAntiforgery();

        app.MapAccountEndpoints();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}
