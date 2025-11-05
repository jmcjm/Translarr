using Microsoft.EntityFrameworkCore;
using Translarr.Core.Infrastructure.Persistence.Configurations;
using Translarr.Core.Infrastructure.Persistence.Daos;

namespace Translarr.Core.Infrastructure.Persistence;

public class TranslarrDbContext(DbContextOptions<TranslarrDbContext> options) : DbContext(options)
{
    public DbSet<ApiUsageDao> ApiUsage { get; set; }
    public DbSet<SubtitleEntryDao> SubtitleEntries { get; set; }
    public DbSet<AppSettingsDao> AppSettings { get; set; }
    public DbSet<SeriesWatchConfigDao> SeriesWatchConfigs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new ApiUsageConfiguration());
        modelBuilder.ApplyConfiguration(new SubtitleEntryConfiguration());
        modelBuilder.ApplyConfiguration(new AppSettingsConfiguration());
        modelBuilder.ApplyConfiguration(new SeriesWatchConfigConfiguration());
    }
}
