using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Translarr.Core.Application.Abstractions.Repositories;
using Translarr.Core.Application.Abstractions.Services;
using Translarr.Core.Application.Services;
using Translarr.Core.Infrastructure.Persistence;
using Translarr.Core.Infrastructure.Repositories;
using Translarr.Core.Infrastructure.Services;

namespace Translarr.Core.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Database - uses connection string from Aspire or fallback for development
        var connectionString = configuration.GetConnectionString("translarr-db") ?? throw new ArgumentException("Connection string not found.");
        
        services.AddDbContext<TranslarrDbContext>(options =>
            options.UseSqlite(connectionString));
        
        // Database Initializer
        services.AddScoped<TranslarrDatabaseInitializer>();
        
        // Repositories
        services.AddScoped<ISubtitleEntryRepository, SubtitleEntryRepository>();
        services.AddScoped<IAppSettingsRepository, AppSettingsRepository>();
        services.AddScoped<IApiUsageRepository, ApiUsageRepository>();
        
        // Application Services
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IApiUsageService, ApiUsageService>();
        services.AddScoped<IMediaScannerService, MediaScannerService>();
        services.AddScoped<ISubtitleTranslationService, SubtitleTranslationService>();
        
        // Infrastructure Services
        services.AddScoped<IFfmpegService, FfmpegService>();
        services.AddScoped<IGeminiClient, GeminiClient>();
        
        return services;
    }
    
    /// <summary>
    /// Initialize database and seed default settings
    /// </summary>
    public static async Task InitializeDatabaseAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<TranslarrDatabaseInitializer>();
        await initializer.InitializeAsync();
    }
}

