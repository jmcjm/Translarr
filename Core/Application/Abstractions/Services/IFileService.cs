namespace Translarr.Core.Application.Abstractions.Services;

public interface IFileService
{
    Task WriteTextAsync(string path, string content);
    bool Exists(string path);
}
