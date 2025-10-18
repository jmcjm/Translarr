using Translarr.Core.Api.Endpoints;
using Translarr.Core.Api.Middleware;
using Translarr.Core.Infrastructure;

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

        // Add Swagger/OpenAPI
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new()
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
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "Translarr API v1");
                options.RoutePrefix = "swagger"; // Swagger will be available at /swagger
            });
        }

        // Use exception handler
        app.UseExceptionHandler();

        app.UseHttpsRedirection();
        app.UseCors();
        app.UseAuthorization();

        // Map API endpoints
        var apiGroup = app.MapGroup("/api")
            .WithOpenApi();

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

        app.Run();
    }
}