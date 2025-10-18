using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Translarr.Core.Infrastructure.Persistence.Daos;

namespace Translarr.Core.Infrastructure.Persistence.Configurations;

public class AppSettingsConfiguration : IEntityTypeConfiguration<AppSettingsDao>
{
    public void Configure(EntityTypeBuilder<AppSettingsDao> builder)
    {
        builder.ToTable("app_settings");
        
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd()
            .IsRequired();
        
        builder.Property(x => x.Key)
            .IsRequired()
            .HasMaxLength(128);
        
        builder.Property(x => x.Value)
            .IsRequired()
            .HasMaxLength(8192); // Large limit for SystemPrompt
        
        builder.Property(x => x.Description)
            .HasMaxLength(512);
        
        builder.Property(x => x.UpdatedAt)
            .IsRequired();
        
        // Unikalny indeks na Key
        builder.HasIndex(x => x.Key)
            .IsUnique();
    }
}

