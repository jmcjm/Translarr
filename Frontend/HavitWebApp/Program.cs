using Havit.Blazor.Components.Web;
using Translarr.Frontend.HavitWebApp.Components;
using Translarr.Frontend.HavitWebApp.Services;
using Translarr.Frontend.Shared.Services;
using Translarr.ServiceDefaults;

namespace Translarr.Frontend.HavitWebApp;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();

        // Add Blazor services
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // Add Havit.Blazor services
        builder.Services.AddHxServices();
        builder.Services.AddHxMessenger();

        // Add Theme Service
        builder.Services.AddScoped<ThemeService>();

        // Configure HttpClient for API communication
        // In Docker Compose: uses ApiBaseUrl from environment variable
        // With Aspire: uses service discovery (https+http://Translarr-Api)
        var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https+http://Translarr-Api";
        builder.Services.AddHttpClient("TranslarrApi", client =>
        {
            client.BaseAddress = new Uri(apiBaseUrl);
        });

        // Register API services
        builder.Services.AddScoped<LibraryApiService>();
        builder.Services.AddScoped<TranslationApiService>();
        builder.Services.AddScoped<SettingsApiService>();
        builder.Services.AddScoped<StatsApiService>();
        builder.Services.AddScoped<SeriesWatchApiService>();

        var app = builder.Build();

        app.MapDefaultEndpoints();

        // Configure the HTTP request pipeline
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}
