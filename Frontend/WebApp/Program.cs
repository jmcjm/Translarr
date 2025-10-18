using MudBlazor.Services;
using Translarr.Frontend.WebApp.Components;
using Translarr.Frontend.WebApp.Services;

namespace Translarr.Frontend.WebApp;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();

        // Add Blazor services
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // Add MudBlazor services
        builder.Services.AddMudServices();

        // Add Theme Service
        builder.Services.AddScoped<ThemeService>();

        // Configure HttpClient for API communication using Aspire Service Discovery
        // "api" is the service name from AppHost - Aspire will automatically resolve the URL
        builder.Services.AddHttpClient("TranslarrApi", client =>
        {
            // https+http://api means: use HTTPS if available, fallback to HTTP
            // "api" is the name from AppHost.cs: builder.AddProject<Projects.Api>("api")
            client.BaseAddress = new Uri("https+http://api");
        });

        // Register API services
        builder.Services.AddScoped<LibraryApiService>();
        builder.Services.AddScoped<TranslationApiService>();
        builder.Services.AddScoped<SettingsApiService>();
        builder.Services.AddScoped<StatsApiService>();

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