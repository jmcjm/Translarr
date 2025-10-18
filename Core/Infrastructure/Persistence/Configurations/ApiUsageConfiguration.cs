using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Translarr.Core.Infrastructure.Persistence.Daos;

namespace Translarr.Core.Infrastructure.Persistence.Configurations;

public class ApiUsageConfiguration : IEntityTypeConfiguration<ApiUsageDao>
{
    public void Configure(EntityTypeBuilder<ApiUsageDao> builder)
    {
        builder.ToTable("api_usage");
        
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd()
            .IsRequired();
            
        builder.Property(x => x.Model)
            .IsRequired();
            
        builder.Property(x => x.Date)
            .IsRequired();
    }
}