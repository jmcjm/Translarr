using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;
using SwaggerThemes;
using Translarr.Core.Api.Endpoints;
using Translarr.Core.Api.Middleware;
using Translarr.Core.Infrastructure;
using Translarr.ServiceDefaults;

namespace Translarr.Core.Api;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();

        // Add Infrastructure services (DbContext, Repositories, Services)
        builder.Services.AddInfrastructure(builder.Configuration);

        // Add auth (Identity, cookie, Data Protection)
        builder.Services.AddTranslarrAuth(builder.Configuration);

        builder.Services.AddAuthorization();

        // Forwarded headers for reverse proxy / Cloudflare Tunnel
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = null;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        // Rate limiting on login endpoint
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("login", limiter =>
            {
                limiter.PermitLimit = 5;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
        });

        // Global exception handler
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();

        // Swagger - registered always, but only exposed in Development
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Translarr API",
                Version = "v1",
                Description = "API for automatic subtitle translation using Gemini AI"
            });
        });

        var app = builder.Build();

        // Initialize databases
        await DependencyInjection.InitializeDatabaseAsync(app.Services);
        await AuthDependencyInjection.InitializeAuthDatabaseAsync(app.Services);

        app.MapDefaultEndpoints();

        // Forwarded headers MUST be before auth and rate limiter
        app.UseForwardedHeaders();

        // Swagger - development only
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(
                Theme.UniversalDark,
                setupAction: c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Translarr API v1");
                    c.RoutePrefix = "swagger";
                });
        }

        app.UseExceptionHandler();

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        // Map API endpoints
        var apiGroup = app.MapGroup("/api");

        apiGroup.MapGroup("/auth")
            .MapAuthEndpoints()
            .WithTags("Authentication");

        apiGroup.MapGroup("/library")
            .MapLibraryEndpoints()
            .RequireAuthorization()
            .WithTags("Library");

        apiGroup.MapGroup("/translation")
            .MapTranslationEndpoints()
            .RequireAuthorization()
            .WithTags("Translation");

        apiGroup.MapGroup("/settings")
            .MapSettingsEndpoints()
            .RequireAuthorization()
            .WithTags("Settings");

        apiGroup.MapGroup("/stats")
            .MapStatsEndpoints()
            .RequireAuthorization()
            .WithTags("Statistics");

        apiGroup.MapGroup("/series")
            .MapSeriesWatchEndpoints()
            .RequireAuthorization()
            .WithTags("Series Watch");

        await app.RunAsync();
    }
}
