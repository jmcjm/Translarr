using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Translarr.Core.Infrastructure.Persistence.Daos;

namespace Translarr.Core.Infrastructure.Persistence.Configurations;

public class SubtitleEntryConfiguration : IEntityTypeConfiguration<SubtitleEntryDao>
{
    public void Configure(EntityTypeBuilder<SubtitleEntryDao> builder)
    {
        builder.ToTable("subtitle_entries");
        
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd()
            .IsRequired();
        
        builder.Property(x => x.Series)
            .IsRequired()
            .HasMaxLength(256);
        
        builder.Property(x => x.Season)
            .IsRequired()
            .HasMaxLength(256);
        
        builder.Property(x => x.FileName)
            .IsRequired()
            .HasMaxLength(512);
        
        builder.Property(x => x.FilePath)
            .IsRequired()
            .HasMaxLength(2048);
        
        builder.Property(x => x.IsProcessed)
            .IsRequired()
            .HasDefaultValue(false);
        
        builder.Property(x => x.IsWanted)
            .IsRequired()
            .HasDefaultValue(false);
        
        builder.Property(x => x.AlreadyHas)
            .IsRequired()
            .HasDefaultValue(false);
        
        builder.Property(x => x.LastScanned)
            .IsRequired();
        
        builder.Property(x => x.ProcessedAt);
        
        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(2048);
        
        // Indeksy dla efektywnego wyszukiwania
        builder.HasIndex(x => new { x.IsProcessed, x.IsWanted, x.AlreadyHas });
        builder.HasIndex(x => x.FilePath).IsUnique();
        builder.HasIndex(x => new { x.Series, x.Season });
    }
}

