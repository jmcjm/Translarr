using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Translarr.Core.Infrastructure.Persistence;
using Translarr.Core.Infrastructure.Persistence.Daos;

namespace Translarr.Core.Infrastructure.Services;

public class TranslarrDatabaseInitializer(TranslarrDbContext context, ILogger<TranslarrDatabaseInitializer> logger)
{
    public async Task InitializeAsync()
    {
        try
        {
            await CheckConnectionAsync();
            logger.LogInformation("Applying migrations...");
            await context.Database.MigrateAsync();
            logger.LogInformation("Migrations applied successfully");
            
            await SeedDefaultSettingsAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while initializing the database");
            throw;
        }
    }
    
    private async Task CheckConnectionAsync(int maxRetries = 3, int delayMilliseconds = 1000)
    {
        logger.LogInformation("Checking database connection...");
        
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var canConnect = await context.Database.CanConnectAsync();
                
                if (canConnect)
                {
                    logger.LogInformation("Database connection successful on attempt {Attempt}", attempt);
                    return;
                }
                
                logger.LogWarning("Database connection failed on attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Database connection exception on attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
            }
            
            if (attempt < maxRetries)
            {
                await Task.Delay(delayMilliseconds * attempt);
            }
        }
        
        logger.LogError("Failed to connect to database after {MaxRetries} attempts", maxRetries);
        throw new InvalidOperationException($"Unable to connect to database after {maxRetries} attempts");
    }
    
    private async Task SeedDefaultSettingsAsync()
    {
        logger.LogInformation("Seeding default settings if needed");
        
        var defaultSettings = new List<(string Key, string Value, string Description)>
        {
            ("GeminiApiKey", "", "Google Gemini API key – required for subtitle translation"),
            ("GeminiModel", "gemini-2.5-pro", "Name of the Google Gemini model to use"),
            ("Temperature", "0.55", "AI model temperature (0.0 - 1.0). Lower value = more deterministic translation"),
            ("SystemPrompt", 
                "You are an advanced subtitle translator to polish. " +
                "Translate the provided subtitles. " +
                "Preserve the original formatting, tags, and most importantly, do not change timestamps.", 
                "System prompt for the AI specifying how it should translate subtitles"),
            ("PreferredSubsLang", "pl", "Target subtitle language code (e.g. 'pl', 'pr', 'uk')"),
            ("RateLimitPerMinute", "5", "Maximum number of API requests per minute"),
            ("RateLimitPerDay", "100", "Maximum number of API requests per day"),
            ("AutoLibraryScan", "false", "Whether to automatically scan the library (requires Worker Service – not yet available)"),
            ("AutoTranslate", "false", "Whether to automatically translate new subtitles (requires Worker Service – not yet available)")
        };
        
        var addedCount = 0;
        
        foreach (var (key, value, description) in defaultSettings)
        {
            // Check if the setting already exists
            var existingSetting = await context.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
            
            if (existingSetting == null)
            {
                // Add if it doesn't exist yet
                var newSetting = new AppSettingsDao
                {
                    Key = key,
                    Value = value,
                    Description = description,
                    UpdatedAt = DateTime.UtcNow
                };
                
                await context.AppSettings.AddAsync(newSetting);
                addedCount++;
                
                logger.LogInformation("Added new setting: {Key}", key);
            }
            else
            {
                logger.LogDebug("Setting {Key} already exists, skipping", key);
            }
        }
        
        if (addedCount > 0)
        {
            await context.SaveChangesAsync();
            logger.LogInformation("Successfully seeded {Count} new default settings", addedCount);
        }
        else
        {
            logger.LogInformation("All default settings already exist, no seeding needed");
        }
    }
}