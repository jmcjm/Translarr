using Microsoft.Data.Sqlite;
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

        // Configure SQLite connection with WAL mode and shared cache
        var builder = new SqliteConnectionStringBuilder(connectionString)
        {
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        services.AddDbContext<TranslarrDbContext>(options =>
        {
            options.UseSqlite(builder.ToString(), sqliteOptions =>
            {
                sqliteOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });
        });
        
        // Database Initializer
        services.AddScoped<TranslarrDatabaseInitializer>();
        
        // Unit of Work
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Repositories
        services.AddScoped<ISubtitleEntryRepository, SubtitleEntryRepository>();
        services.AddScoped<IAppSettingsRepository, AppSettingsRepository>();
        services.AddScoped<IApiUsageRepository, ApiUsageRepository>();
        services.AddScoped<ISeriesWatchConfigRepository, SeriesWatchConfigRepository>();
        
        // Application Services
        services.AddScoped<ILibraryService, LibraryService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IApiUsageService, ApiUsageService>();
        services.AddScoped<IMediaScannerService, MediaScannerService>();
        services.AddScoped<ISubtitleTranslationService, SubtitleTranslationService>();
        services.AddScoped<ISeriesWatchService, SeriesWatchService>();
        
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

