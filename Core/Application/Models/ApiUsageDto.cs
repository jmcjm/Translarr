namespace Translarr.Core.Application.Models;

public class ApiUsageDto
{
    public uint? Id { get; set; }
    public required string Model { get; set; }
    public DateTime Date { get; set; }
}

