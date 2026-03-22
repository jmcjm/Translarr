namespace Translarr.Core.Infrastructure.Services;

public record RenderedFrame(string FilePath, TimeSpan StartTime, TimeSpan EndTime, int Index);
