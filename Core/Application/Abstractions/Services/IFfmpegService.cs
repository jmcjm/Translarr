using Translarr.Core.Application.Models;

namespace Translarr.Core.Application.Abstractions.Services;

public interface IFfmpegService
{
    Task<object> GetVideoStreamsAsync(string videoPath);
    Task<SubtitleStreamInfo?> FindBestSubtitleStreamAsync(string videoPath);
    Task<bool> ExtractSubtitlesAsync(string videoPath, int streamIndex, string outputPath);
}

