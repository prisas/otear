using Otear.Mpv;

namespace Otear.Mpv.Tests;

[TestClass]
public sealed class ScalerPolicyTests
{
    private static MpvVideoParams Vp(int w, int h, string hwFormat, long rotate = 0) =>
        new(w, h, w, h, "d3d11", hwFormat, "bt.709", "limited", "bt.709", "bt.1886", null, rotate, null, null, null);

    [TestMethod]
    [DataRow(3840, 2160, "p010", 1920, 1080, "bilinear")]  // 4K 4:2:0 in a 1080p window: chroma ends up at 1:1
    [DataRow(3840, 2160, "nv12", 1600, 900, "bilinear")]
    [DataRow(3840, 2160, "p010", 2103, 1183, "bilinear")]  // auto-fit window: chroma enlarged only 1.1x
    [DataRow(3840, 2160, "p010", 2560, 1440, "")]          // shown 1.33x larger than the chroma plane: keep quality
    [DataRow(3840, 2160, "p010", 3840, 2160, "")]
    [DataRow(1920, 1080, "nv12", 1920, 1080, "")]          // 1080p 1:1 still needs chroma upscaling
    [DataRow(1920, 1080, "nv12", 960, 540, "bilinear")]
    [DataRow(3840, 2160, "yuv444p10", 1280, 720, "")]      // no subsampling: leave mpv's default alone
    [DataRow(3840, 2160, "y210", 1920, 1080, "bilinear")]  // 4:2:2: chroma is 1920x2160, shown 1920x1080
    [DataRow(3840, 2160, "y210", 2560, 1440, "")]          // shown wider than the chroma plane
    [DataRow(3840, 1600, "p010", 1920, 1080, "bilinear")]  // letterboxed scope film
    public void PicksBilinearOnlyWhenChromaIsNotEnlarged(int w, int h, string fmt, int outW, int outH, string expected)
    {
        Assert.AreEqual(expected, ScalerPolicy.ChromaScaler(Vp(w, h, fmt), outW, outH));
    }

    [TestMethod]
    public void RotationSwapsAxes()
    {
        // Portrait phone video 1080x1920 (stored 1920x1080, rotated 90°) shown 540 wide x 960 high.
        var vp = new MpvVideoParams(1920, 1080, 1920, 1080, "d3d11", "nv12", null, null, null, null, null, 90, null, null, null);
        Assert.AreEqual("bilinear", ScalerPolicy.ChromaScaler(vp, 540, 960));
        Assert.AreEqual("", ScalerPolicy.ChromaScaler(vp, 1080, 1920));
    }

    [TestMethod]
    public void UnknownInputKeepsDefault()
    {
        Assert.AreEqual("", ScalerPolicy.ChromaScaler(null, 1920, 1080));
        Assert.AreEqual("", ScalerPolicy.ChromaScaler(Vp(3840, 2160, "p010"), 0, 0));
    }

    [TestMethod]
    public void CscaleCanBeResetToItsDefaultAtRuntime()
    {
        using var p = new TestPlayer();
        p.Client.SetProperty("cscale", "bilinear");
        Assert.AreEqual("bilinear", p.Client.GetString("cscale"));
        p.Client.SetProperty("cscale", ScalerPolicy.DefaultChroma);
        Assert.AreEqual("", p.Client.GetString("cscale"));
    }
}
