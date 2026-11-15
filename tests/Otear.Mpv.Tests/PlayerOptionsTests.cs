using Otear.Mpv;

namespace Otear.Mpv.Tests;

[TestClass]
public sealed class PlayerOptionsTests
{
    private static PlayerOptions Sample(bool everything) => new()
    {
        WatchLaterDirectory = Path.GetTempPath(),
        ShaderCacheDirectory = Path.GetTempPath(),
        ScreenshotDirectory = Path.GetTempPath(),
        Quality = everything ? VideoQuality.High : VideoQuality.Balanced,
        SmoothMotion = everything,
        AudioPassthrough = everything,
        PreferredAudioLanguages = everything ? "jpn,ja" : null,
        PreferredSubtitleLanguages = everything ? "eng,en" : null,
        SubtitleFont = everything ? "Segoe UI" : null,
        ConfigDirectory = everything ? Path.GetTempPath() : null,
        LogFile = everything ? Path.Combine(Path.GetTempPath(), "otear-test-mpv.log") : null,
        ExtraOptions = everything ? "# comment\n--sub-scale=1.1\nvolume-max = 150\n" : null,
    };

    [TestMethod]
    public void EveryOptionNameExistsInLibmpv()
    {
        using var p = new TestPlayer();
        var known = ((List<object?>)p.Client.GetNode("options")!).Cast<string>().ToHashSet(StringComparer.Ordinal);
        known.Add("profile");
        var missing = Sample(true).Build().Concat(Sample(false).Build())
            .Select(kv => kv.Key).Distinct().Where(k => !known.Contains(k)).ToArray();
        Assert.IsEmpty(missing, "Unknown mpv options: " + string.Join(", ", missing));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void OptionsInitializeACore(bool everything)
    {
        var opts = Sample(everything).Build()
            .Where(kv => kv.Key is not ("vo" or "gpu-api" or "gpu-context" or "d3d11-output-mode" or "config-dir"))
            .Append(new("vo", "null")).Append(new("ao", "null"));
        using var client = MpvClient.Create(opts);
        Assert.AreEqual("no", client.GetString("osc"));
        Assert.AreEqual(everything ? 1.1 : 1.0, client.GetDouble("sub-scale")!.Value, 1e-5);
        Assert.AreEqual(everything ? 150.0 : 130.0, client.GetDouble("volume-max")!.Value, 1e-9);
    }

    [TestMethod]
    public void PowerSavingUsesTheFastProfile()
    {
        var opts = (Sample(false) with { Quality = VideoQuality.PowerSaving }).Build()
            .Where(kv => kv.Key is not ("vo" or "gpu-api" or "gpu-context" or "d3d11-output-mode"))
            .Append(new("vo", "null")).Append(new("ao", "null"));
        using var client = MpvClient.Create(opts);
        Assert.AreEqual("bilinear", client.GetString("scale"));
        Assert.AreEqual("no", client.GetString("hdr-compute-peak"));
    }

    [TestMethod]
    public void ParsesExtraOptions()
    {
        var extra = PlayerOptions.ParseExtra("a=1\n\n# x\n--b = two words\nflag").ToArray();
        CollectionAssert.AreEqual(new[] { "a", "b", "flag" }, extra.Select(e => e.Key).ToArray());
        CollectionAssert.AreEqual(new[] { "1", "two words", "yes" }, extra.Select(e => e.Value).ToArray());
    }
}
