using Otear.Mpv;

namespace Otear.Mpv.Tests;

[TestClass]
public sealed class ToneMappingTests
{
    private static MpvVideoParams Vp(string gamma, double? maxCll = null, double? maxLuma = null) =>
        new(3840, 2160, 3840, 2160, "p010", null, "bt.2020-ncl", "limited", "bt.2020", gamma, null, 0, maxCll, null, maxLuma);

    [TestMethod]
    [DataRow("bt.1886", null, null, true, 1300.0, "no")]   // SDR content: nothing to tone-map
    [DataRow("bt.1886", null, null, false, 0.0, "no")]
    [DataRow("pq", 1000.0, 1000.0, false, 0.0, "auto")]    // HDR on an SDR display: tone-map with detection
    [DataRow("pq", 1000.0, 1000.0, true, 1300.0, "no")]    // HDR10 fits the HDR display: pure passthrough
    [DataRow("pq", 4000.0, 4000.0, true, 1300.0, "auto")]  // brighter than the display: tone-map
    [DataRow("pq", null, 4000.0, true, 1300.0, "auto")]    // no MaxCLL: use the mastering peak
    [DataRow("pq", 0.0, 0.0, true, 1300.0, "no")]          // no metadata: assume 1000 nits
    [DataRow("hlg", null, null, true, 600.0, "auto")]      // HLG on a dim HDR display
    [DataRow("hlg", null, null, true, 1300.0, "no")]
    [DataRow("pq", 1000.0, 1000.0, true, 0.0, "no")]       // unknown display peak: assume 1000 nits
    public void AutoModeDetectsPeaksOnlyWhenToneMapping(string gamma, double? maxCll, double? maxLuma, bool hdrOutput, double displayPeak, string expected)
    {
        Assert.AreEqual(expected, ToneMapping.PeakDetectionValue(PeakDetectionMode.Auto, Vp(gamma, maxCll, maxLuma), false, hdrOutput, displayPeak));
    }

    [TestMethod]
    public void DolbyVisionNeverNeedsDetectionInAutoMode() =>
        Assert.AreEqual("no", ToneMapping.PeakDetectionValue(PeakDetectionMode.Auto, Vp("pq", 4000, 4000), true, false, 0));

    [TestMethod]
    public void ExplicitModesWin()
    {
        Assert.AreEqual("yes", ToneMapping.PeakDetectionValue(PeakDetectionMode.Always, Vp("bt.1886"), false, true, 1300));
        Assert.AreEqual("no", ToneMapping.PeakDetectionValue(PeakDetectionMode.Off, Vp("pq", 4000, 4000), false, false, 0));
    }

    [TestMethod]
    public void NoVideoMeansNoDetection() =>
        Assert.AreEqual("no", ToneMapping.PeakDetectionValue(PeakDetectionMode.Auto, null, false, false, 0));
}
