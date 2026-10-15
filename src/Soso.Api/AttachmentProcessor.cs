using ImageMagick;

namespace Soso.Api;

/// <summary>
/// Shared attachment validation for the browser REST endpoints and the MCP tools,
/// so both transports enforce identical limits and normalization.
/// </summary>
internal static class AttachmentProcessor
{
    public const int MaxImageBytes = 5 * 1024 * 1024;
    public const int MaxVideoBytes = 50 * 1024 * 1024;
    public static readonly string[] AllowedVideoTypes = ["video/mp4", "video/webm", "video/ogg"];

    private static readonly SemaphoreSlim ImageGate = new(2);
    private static readonly SemaphoreSlim VideoGate = new(2);

    public static async Task<T> WithImageGate<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        var acquired = await ImageGate.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        if (!acquired)
        {
            throw new ApiException(429, "Image processing is busy. Try again shortly.");
        }
        try
        {
            return await operation();
        }
        finally
        {
            ImageGate.Release();
        }
    }

    public static async Task<T> WithVideoGate<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        var acquired = await VideoGate.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        if (!acquired)
        {
            throw new ApiException(429, "Video processing is busy. Try again shortly.");
        }
        try
        {
            return await operation();
        }
        finally
        {
            VideoGate.Release();
        }
    }

    /// <summary>Validates raw image bytes and normalizes them to stripped PNG (max 1600px side).</summary>
    public static byte[] DecodeImageBytes(byte[] input, CancellationToken cancellationToken = default)
    {
        if (input is not { Length: > 0 } || input.Length > MaxImageBytes)
        {
            throw new ApiException(400, "Choose an image under 5 MB.");
        }
        try
        {
            using var information = new MagickImageCollection();
            information.Ping(input);
            var first = information.Count == 0 ? null : information[0];
            var allowed = first is not null && ImageProcessing.AllowedFormats.Contains(first.Format);
            if (!allowed || information.Count != 1 || (long)first!.Width * first.Height > 16_000_000)
            {
                throw new ApiException(400, "Use a single-frame PNG, JPEG, GIF, WebP, BMP or TIFF image up to 16 megapixels.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            using var image = new MagickImage(input);
            image.Resize(new MagickGeometry(1600, 1600) { Greater = true });
            image.Strip();
            var bytes = image.ToByteArray(MagickFormat.Png);
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes.Length > MaxImageBytes)
            {
                throw new ApiException(400, "Processed image is too large.");
            }
            return bytes;
        }
        catch (MagickException)
        {
            throw new ApiException(400, "Invalid image file.");
        }
    }

    /// <summary>Validates raw video bytes plus a thumbnail image, converting the video to browser MP4.</summary>
    public static async Task<(byte[] Content, string ContentType, byte[] Thumbnail)> ValidateVideoUploadAsync(
        byte[] content, string contentType, byte[] thumbnail, CancellationToken cancellationToken)
    {
        if (content is not { Length: > 0 } || content.Length > MaxVideoBytes)
        {
            throw new ApiException(400, "Choose a video under 50 MB.");
        }
        var normalized = contentType.ToLowerInvariant();
        if (!AllowedVideoTypes.Contains(normalized))
        {
            throw new ApiException(400, "Use an MP4, WebM or Ogg video.");
        }
        var thumbnailBytes = DecodeImageBytes(thumbnail, cancellationToken);
        if (!HasVideoSignature(content, normalized))
        {
            throw new ApiException(400, "The video content does not match its file type.");
        }
        var converted = await VideoTranscoder.ConvertToBrowserMp4Async(content, cancellationToken);
        return (converted, "video/mp4", thumbnailBytes);
    }

    private static bool HasVideoSignature(byte[] content, string contentType) => contentType switch
    {
        "video/mp4" => content.Length >= 8 && content.AsSpan(4, 4).SequenceEqual("ftyp"u8),
        "video/webm" => content.AsSpan().StartsWith(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }),
        "video/ogg" => content.AsSpan().StartsWith("OggS"u8),
        _ => false
    };
}
