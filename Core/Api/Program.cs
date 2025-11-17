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
        
        builder.AddSqliteConnection("sqlite");

        // Add Infrastructure services (DbContext, Repositories, Services)
        builder.Services.AddInfrastructure(builder.Configuration);

        // Add services to the container
        builder.Services.AddAuthorization();

        // Add global exception handler
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();

        // Add Swagger/OpenAPI with Swashbuckle
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

        // Add CORS for Blazor frontend
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        var app = builder.Build();

        // Initialize database
        await DependencyInjection.InitializeDatabaseAsync(app.Services);

        app.MapDefaultEndpoints();

        // Configure the HTTP request pipeline
        // if (app.Environment.IsDevelopment())
        // {
        app.UseSwagger();
        app.UseSwaggerUI(
            Theme.UniversalDark,
            setupAction: c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Translarr API v1");
                c.RoutePrefix = "swagger";
            });
        // }

        // Use exception handler
        app.UseExceptionHandler();

        app.UseHttpsRedirection();
        app.UseCors();
        app.UseAuthorization();

        // Map API endpoints
        var apiGroup = app.MapGroup("/api");

        apiGroup.MapGroup("/library")
            .MapLibraryEndpoints()
            .WithTags("Library");

        apiGroup.MapGroup("/translation")
            .MapTranslationEndpoints()
            .WithTags("Translation");

        apiGroup.MapGroup("/settings")
            .MapSettingsEndpoints()
            .WithTags("Settings");

        apiGroup.MapGroup("/stats")
            .MapStatsEndpoints()
            .WithTags("Statistics");

        apiGroup.MapGroup("/series")
            .MapSeriesWatchEndpoints()
            .WithTags("Series Watch");

        app.Run();
    }
}