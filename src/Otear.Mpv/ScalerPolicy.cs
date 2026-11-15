namespace Otear.Mpv;

/// <summary>
/// Picks mpv's chroma scaler (<c>cscale</c>) per frame size. With 4:2:0/4:2:2 video, colour planes are first
/// upscaled to the luma size. When the picture is then shown no larger than the colour planes themselves
/// (e.g. 4K 4:2:0 in a 1080p/1440p-or-smaller window), that chroma upscale is undone by the downscale, so a
/// cheap bilinear filter gives the same image as lanczos while saving a large share of GPU time
/// (measured: ~35 % of mpv's GPU time for 4K60 HDR on an RTX 4080 SUPER).
/// </summary>
public static class ScalerPolicy
{
    /// <summary>Chroma subsampling factors (x, y) of a pixel format name, e.g. nv12/p010 → (2, 2).</summary>
    public static (int X, int Y) Subsampling(string? pixelFormat)
    {
        string f = (pixelFormat ?? string.Empty).ToLowerInvariant();
        if (f.Length == 0) return (2, 2);
        if (f.Contains("420") || f is "nv12" or "nv21" or "p010" or "p012" or "p016" or "nv15" or "nv20" or "yuvj420p") return (2, 2);
        if (f.Contains("422") || f.Contains("yuyv") || f.Contains("uyvy") || f is "nv16" or "y210" or "y212" or "p210" or "p216") return (2, 1);
        if (f.Contains("411")) return (4, 1);
        if (f.Contains("410")) return (4, 4);
        return (1, 1); // 4:4:4, RGB, grey: no chroma upscaling at all
    }

    /// <summary>
    /// Value for <c>cscale</c>: "bilinear" when chroma upscaling cannot affect the shown picture,
    /// otherwise <see cref="DefaultChroma"/> (mpv's default, i.e. follow <c>scale</c>).
    /// </summary>
    public static string ChromaScaler(MpvVideoParams? vp, int outputWidth, int outputHeight)
    {
        if (vp is null || vp.Width <= 0 || vp.Height <= 0 || outputWidth <= 0 || outputHeight <= 0) return DefaultChroma;
        var (sx, sy) = Subsampling(vp.HwPixelFormat ?? vp.PixelFormat);
        if (sx == 1 && sy == 1) return DefaultChroma;

        bool rotated = vp.Rotate % 180 != 0;
        double dw = rotated ? vp.DisplayHeight : vp.DisplayWidth;
        double dh = rotated ? vp.DisplayWidth : vp.DisplayHeight;
        if (dw <= 0 || dh <= 0) return DefaultChroma;
        double fit = Math.Min(outputWidth / dw, outputHeight / dh);
        double shownW = dw * fit, shownH = dh * fit;

        double chromaW = (rotated ? vp.Height : vp.Width) / (double)(rotated ? sy : sx);
        double chromaH = (rotated ? vp.Width : vp.Height) / (double)(rotated ? sx : sy);
        return shownW <= chromaW * MaxChromaEnlargement + 0.5 && shownH <= chromaH * MaxChromaEnlargement + 0.5
            ? "bilinear" : DefaultChroma;
    }

    /// <summary>
    /// Colour planes may be enlarged by up to this factor before the filter choice matters: chroma is already
    /// band-limited, so bilinear and lanczos are indistinguishable at small ratios (e.g. 4K in a 2100-px window).
    /// </summary>
    public const double MaxChromaEnlargement = 1.15;

    /// <summary>mpv's own default for <c>cscale</c> (empty: use the <c>scale</c> filter).</summary>
    public const string DefaultChroma = "";
}
