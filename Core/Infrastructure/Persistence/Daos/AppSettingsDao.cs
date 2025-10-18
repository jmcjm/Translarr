namespace Translarr.Core.Infrastructure.Persistence.Daos;

public class AppSettingsDao
{
    public int Id { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
    public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; }
}

