namespace Otear.Mpv;

public enum PeakDetectionMode { Auto, Always, Off }

/// <summary>
/// Decides when libplacebo's per-frame HDR peak detection (<c>hdr-compute-peak</c>) is worth its cost.
/// It is a compute pass with a GPU→CPU readback every frame; on D3D11 it costs far more CPU (driver time)
/// than the rest of the renderer together, but it only improves the picture when HDR has to be tone-mapped.
/// </summary>
public static class ToneMapping
{
    /// <summary>Brightest level the content claims (MaxCLL, else mastering peak; HLG is nominally 1000 nits).</summary>
    public static double ContentPeakNits(MpvVideoParams vp) =>
        vp.MaxCll is > 0 ? vp.MaxCll.Value
        : vp.MaxLuma is > 0 ? vp.MaxLuma.Value
        : 1000;

    /// <summary>True when HDR content must be compressed to fit the output (SDR output, or a dimmer display).</summary>
    public static bool NeedsToneMapping(MpvVideoParams? vp, bool hdrOutput, double displayPeakNits)
    {
        if (vp is null || !vp.IsHdr) return false;
        if (!hdrOutput) return true;
        double display = displayPeakNits > 100 ? displayPeakNits : 1000;
        return ContentPeakNits(vp) > display * 1.05;
    }

    /// <summary>Value for mpv's <c>hdr-compute-peak</c> option.</summary>
    public static string PeakDetectionValue(PeakDetectionMode mode, MpvVideoParams? vp, bool dolbyVision, bool hdrOutput, double displayPeakNits) => mode switch
    {
        PeakDetectionMode.Always => "yes",
        PeakDetectionMode.Off => "no",
        // Dolby Vision carries per-scene brightness metadata, so measuring it again is redundant.
        _ => !dolbyVision && NeedsToneMapping(vp, hdrOutput, displayPeakNits) ? "auto" : "no",
    };
}
