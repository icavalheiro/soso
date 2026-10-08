using System.Runtime.CompilerServices;
using ImageMagick;

namespace Soso.Api;

/// <summary>
/// Hardening for the image pipeline: constrains native ImageMagick resources to safe caps and exposes
/// the format allow-list used when decoding uploads. Applied once per process via a module initializer.
/// </summary>
internal static class ImageProcessing
{
    /// <summary>
    /// Formats accepted from user uploads. Matches the browser-oriented set the API previously relied on
    /// from ImageSharp, without widening to every codec bundled with ImageMagick.
    /// </summary>
    public static readonly MagickFormat[] AllowedFormats =
    [
        MagickFormat.Png,
        MagickFormat.Jpeg,
        MagickFormat.Gif,
        MagickFormat.WebP,
        MagickFormat.Bmp,
        MagickFormat.Tiff,
    ];

    [ModuleInitializer]
    internal static void Initialize()
    {
        // 16 MP input ceiling plus headroom for the resize buffer.
        ResourceLimits.Area = 20UL * 1_000_000;
        ResourceLimits.Width = 20_000;
        ResourceLimits.Height = 20_000;
        // Single-image decoding is enforced by the caller; the collection cap still bounds animated headers.
        ResourceLimits.ListLength = 100;
        // 512 MiB memory, 200 MiB disk and 128 MiB per-request ceilings keep the container inside 512 MiB.
        ResourceLimits.Memory = 512UL * 1_024 * 1_024;
        ResourceLimits.Disk = 200UL * 1_024 * 1_024;
        ResourceLimits.MaxMemoryRequest = 128UL * 1_024 * 1_024;
        // 20-second wall-clock cap per decode.
        ResourceLimits.Time = 20;
        ResourceLimits.Thread = 2;
    }
}
