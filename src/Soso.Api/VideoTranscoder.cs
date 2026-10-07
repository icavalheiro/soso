using System.Diagnostics;

namespace Soso.Api;

internal static class VideoTranscoder
{
    public static async Task<byte[]> ConvertToBrowserMp4Async(byte[] source, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "soso-video-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var inputPath = Path.Combine(directory, "input");
        var outputPath = Path.Combine(directory, "output.mp4");

        try
        {
            await File.WriteAllBytesAsync(inputPath, source, cancellationToken);
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? "ffmpeg",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in new[]
            {
                "-y", "-hide_banner", "-loglevel", "error", "-i", inputPath,
                "-map", "0:v:0", "-map", "0:a:0?", "-vf",
                "scale=w='min(1280,iw)':h='min(720,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2",
                "-c:v", "libx264", "-preset", "ultrafast", "-crf", "30", "-threads", "2",
                "-c:a", "aac", "-b:a", "96k", "-ac", "2", "-movflags", "+faststart", outputPath
            })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new ApiException(500, "Video processing could not be started.");
            }

            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                throw new ApiException(400, "The video could not be decoded or converted.");
            }

            var output = await File.ReadAllBytesAsync(outputPath, cancellationToken);
            if (output.Length == 0 || output.Length > 50 * 1024 * 1024)
            {
                throw new ApiException(400, "Processed video is too large.");
            }
            return output;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException)
            {
                // Best effort cleanup of temporary upload data.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort cleanup of temporary upload data.
            }
        }
    }
}
