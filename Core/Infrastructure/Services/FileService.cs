namespace Translarr.Core.Infrastructure.Services;

public class FileService : Application.Abstractions.Services.IFileService
{
    public async Task WriteTextAsync(string path, string content)
    {
        await File.WriteAllTextAsync(path, content);
    }

    public bool Exists(string path)
    {
        return File.Exists(path);
    }
}
