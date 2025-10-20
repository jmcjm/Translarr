using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Translarr.Core.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for Entity Framework migrations.
/// This allows EF tools to create DbContext instances without requiring the full application startup.
/// </summary>
public class TranslarrDbContextFactory : IDesignTimeDbContextFactory<TranslarrDbContext>
{
    public TranslarrDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TranslarrDbContext>();
        
        // Use a default connection string for design-time operations
        // This will be overridden at runtime with the actual connection string from Aspire
        var connectionString = "Data Source=translarr.db";
        optionsBuilder.UseSqlite(connectionString);
        
        return new TranslarrDbContext(optionsBuilder.Options);
    }
}
