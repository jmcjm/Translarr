using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Translarr.Core.Infrastructure.Persistence.Daos;

namespace Translarr.Core.Infrastructure.Persistence.Configurations;

public class SeriesWatchConfigConfiguration : IEntityTypeConfiguration<SeriesWatchConfigDao>
{
    public void Configure(EntityTypeBuilder<SeriesWatchConfigDao> builder)
    {
        builder.ToTable("series_watch_configs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd()
            .IsRequired();

        builder.Property(x => x.SeriesName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(x => x.SeasonName)
            .HasMaxLength(256);

        builder.Property(x => x.AutoWatch)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        // Unique constraint: jedna konfiguracja per seria/sezon
        builder.HasIndex(x => new { x.SeriesName, x.SeasonName })
            .IsUnique();

        // Index dla szybkiego lookup przy skanowaniu
        builder.HasIndex(x => x.SeriesName);
    }
}
