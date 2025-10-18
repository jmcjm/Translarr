namespace Translarr.Core.Infrastructure.Persistence.Daos;

public class ApiUsageDao
{
    public uint? Id { get; set; }
    public required string Model { get; set; }
    public DateTime Date { get; set; }
}