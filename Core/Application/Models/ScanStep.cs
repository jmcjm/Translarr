namespace Translarr.Core.Application.Models;

public enum ScanStep
{
    Starting,
    DiscoveringFiles,
    AnalyzingStreams,
    UpdatingDatabase,
    Completed
}
