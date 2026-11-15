using Otear.Mpv;
using Otear.Mpv.Native;

[assembly: Parallelize(Workers = 4, Scope = ExecutionScope.MethodLevel)]

namespace Otear.Mpv.Tests;

[TestClass]
public sealed class MpvClientTests
{
    [TestMethod]
    public void ClientApiVersionIsAtLeast2_6()
    {
        uint v = MpvClient.ClientApiVersion;
        Assert.IsTrue((v >> 16) >= 2 && (v >> 16 > 2 || (v & 0xFFFF) >= 6), $"client API {v >> 16}.{v & 0xFFFF}");
    }

    [TestMethod]
    public void ReportsMpvVersion()
    {
        using var p = new TestPlayer();
        string? version = p.Client.GetString("mpv-version");
        StringAssert.StartsWith(version, "mpv v0.42");
        Assert.IsFalse(string.IsNullOrEmpty(p.Client.GetString("ffmpeg-version")));
    }

    [TestMethod]
    public void PlaysH264Mp4WithAacAndMovText()
    {
        using var p = new TestPlayer();
        p.Load(TestPlayer.Media("h264_1080p30.mp4"));

        Assert.AreEqual(10, p.Client.GetDouble("duration")!.Value, 0.2);
        var tracks = MpvTrack.ParseList(p.Client.GetNode("track-list"));
        var video = tracks.Single(t => t.Type == MpvTrackType.Video);
        Assert.AreEqual("h264", video.Codec);
        Assert.AreEqual(1920L, video.Width);
        Assert.AreEqual(1080L, video.Height);
        Assert.AreEqual("aac", tracks.Single(t => t.Type == MpvTrackType.Audio).Codec);
        var sub = tracks.Single(t => t.Type == MpvTrackType.Sub);
        Assert.AreEqual("eng", sub.Lang);
        StringAssert.Contains(sub.DisplayName, "English");
    }

    [TestMethod]
    public void HevcMkvExposesTracksChaptersAndSwitchesThem()
    {
        using var p = new TestPlayer();
        p.Load(TestPlayer.Media("hevc_1080p.mkv"));

        var tracks = MpvTrack.ParseList(p.Client.GetNode("track-list"));
        Assert.AreEqual("hevc", tracks.Single(t => t.Type == MpvTrackType.Video).Codec);

        var audio = tracks.Where(t => t.Type == MpvTrackType.Audio).ToArray();
        Assert.HasCount(2, audio);
        CollectionAssert.AreEqual(new[] { "eng", "jpn" }, audio.Select(a => a.Lang).ToArray());
        Assert.AreEqual("Japanese (880 Hz)", audio[1].Title);
        Assert.AreEqual("opus", audio[1].Codec);

        var subs = tracks.Where(t => t.Type == MpvTrackType.Sub && !t.IsExternal).ToArray();
        Assert.IsTrue(tracks.Any(t => t.Type == MpvTrackType.Sub && t.IsExternal), "sidecar .ext.srt should be auto-loaded");
        Assert.HasCount(3, subs);
        CollectionAssert.AreEqual(new[] { "subrip", "subrip", "ass" }, subs.Select(s => s.Codec).ToArray());
        Assert.AreEqual("Spanish", MpvTrack.LanguageName(subs[1].Lang));

        var chapters = MpvChapter.ParseList(p.Client.GetNode("chapter-list"));
        CollectionAssert.AreEqual(new[] { "Opening", "Middle part", "Ending" }, chapters.Select(c => c.Title).ToArray());
        Assert.AreEqual(3.0, chapters[1].Time, 0.01);

        p.Client.Observe("aid", MpvClient.ObserveFormat.Int64);
        p.Client.SetProperty("aid", 2L);
        p.WaitFor<MpvPropertyChange>(c => c.Name == "aid" && c.Value is 2L);

        p.Client.SetProperty("sid", 3L);
        Assert.AreEqual(3L, p.Client.GetInt64("sid"));
        p.Client.SetProperty("sid", "no");
        Assert.AreEqual("no", p.Client.GetString("sid"));
    }

    [TestMethod]
    public void AddsExternalSubtitle()
    {
        using var p = new TestPlayer(("sub-auto", "no"));
        p.Load(TestPlayer.Media("hevc_1080p.mkv"));
        p.Client.Command("sub-add", TestPlayer.Media("hevc_1080p.ext.srt"), "select");
        var tracks = MpvTrack.ParseList(p.Client.GetNode("track-list"));
        var ext = tracks.Single(t => t.Type == MpvTrackType.Sub && t.IsExternal);
        Assert.IsTrue(ext.IsSelected);
    }

    [TestMethod]
    [DataRow("hevc_2160p60_main10_hdr10.mkv", "pq", "bt.2020", 3840L)]
    [DataRow("hevc_2160p_hlg.mp4", "hlg", "bt.2020", 3840L)]
    [DataRow("h264_2160p30.mp4", "bt.1886", "bt.709", 3840L)]
    public void ReportsVideoColorParameters(string clip, string gamma, string primaries, long width)
    {
        using var p = new TestPlayer();
        p.Client.Command("loadfile", TestPlayer.Media(clip));
        p.WaitEvent(MpvEventId.VideoReconfig, 30);
        var vp = MpvVideoParams.Parse(p.Client.GetNode("video-params"));
        Assert.IsNotNull(vp);
        Assert.AreEqual(width, vp.Width);
        Assert.AreEqual(primaries, vp.Primaries);
        Assert.AreEqual(gamma, vp.Gamma);
        if (gamma == "pq")
        {
            StringAssert.Contains(vp.PixelFormat, "10");
            // HDR10 static metadata from the encoder: MaxCLL 1000 and mastering peak 1000 nits.
            Assert.AreEqual(1000, vp.MaxCll ?? 0, 1);
            Assert.AreEqual(1000, vp.MaxLuma ?? 0, 1);
        }
        var track = MpvTrack.ParseList(p.Client.GetNode("track-list")).Single(t => t.Type == MpvTrackType.Video);
        Assert.IsNull(track.DolbyVisionProfile);
    }

    [TestMethod]
    public void SeeksAbsolute()
    {
        using var p = new TestPlayer();
        p.Load(TestPlayer.Media("h264_720p_120s.mp4"));
        p.Client.Command("seek", "61.5", "absolute+exact");
        p.WaitEvent(MpvEventId.PlaybackRestart);
        Assert.AreEqual(61.5, p.Client.GetDouble("time-pos")!.Value, 0.05);
    }

    [TestMethod]
    public async Task AsyncCommandsCompleteAndReportErrors()
    {
        using var p = new TestPlayer();
        await p.Client.CommandAsync("loadfile", TestPlayer.Media("h264_1080p30.mp4"));
        p.WaitEvent(MpvEventId.FileLoaded);
        await p.Client.SetPropertyAsync("speed", 1.5);
        Assert.AreEqual(1.5, p.Client.GetDouble("speed")!.Value, 1e-9);

        var ex = await Assert.ThrowsExactlyAsync<MpvException>(() => p.Client.CommandAsync("no-such-command"));
        Assert.IsLessThan(0, ex.ErrorCode);
    }

    [TestMethod]
    public async Task NamedArgumentCommandNode()
    {
        using var p = new TestPlayer();
        await p.Client.CommandNodeAsync(new Dictionary<string, object?>
        {
            ["name"] = "loadfile",
            ["url"] = TestPlayer.Media("h264_1080p30.mp4"),
            ["flags"] = "replace",
        });
        p.WaitEvent(MpvEventId.FileLoaded);
        var playlist = MpvPlaylistEntry.ParseList(p.Client.GetNode("playlist"));
        Assert.HasCount(1, playlist);
        Assert.AreEqual("h264_1080p30.mp4", playlist[0].DisplayName);
    }

    [TestMethod]
    public void ResumesFromWatchLaterPosition()
    {
        string dir = Path.Combine(Path.GetTempPath(), "otear-watch-later-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string clip = TestPlayer.Media("h264_720p_120s.mp4");
            using (var p = new TestPlayer(("watch-later-dir", dir), ("resume-playback", "yes"), ("watch-later-options", "start,aid,sid")))
            {
                p.Load(clip);
                p.Client.Command("seek", "42", "absolute+exact");
                p.WaitEvent(MpvEventId.PlaybackRestart);
                p.Client.Command("write-watch-later-config");
            }
            Assert.IsNotEmpty(Directory.GetFiles(dir));

            using (var p = new TestPlayer(("watch-later-dir", dir), ("resume-playback", "yes")))
            {
                p.Load(clip);
                Assert.AreEqual(42, p.Client.GetDouble("time-pos")!.Value, 1.0);
                p.Client.Command("delete-watch-later-config");
            }
            Assert.IsEmpty(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void MissingFileEndsWithError()
    {
        using var p = new TestPlayer();
        p.Client.Command("loadfile", Path.Combine(Path.GetTempPath(), "otear-does-not-exist.mkv"));
        var end = p.WaitFor<MpvEndFile>();
        Assert.IsTrue(end.IsError);
        Assert.IsFalse(string.IsNullOrEmpty(end.ErrorMessage));
    }

    [TestMethod]
    public void AudioOnlyFileHasMetadataAndNoVideo()
    {
        using var p = new TestPlayer();
        p.Load(TestPlayer.Media("audio_only.m4a"));
        var tracks = MpvTrack.ParseList(p.Client.GetNode("track-list"));
        Assert.IsFalse(tracks.Any(t => t.Type == MpvTrackType.Video));
        var meta = p.Client.GetNode("metadata") as IReadOnlyDictionary<string, object?>;
        Assert.IsNotNull(meta);
        Assert.AreEqual("Otear Test Suite", meta.Single(kv => kv.Key.Equals("artist", StringComparison.OrdinalIgnoreCase)).Value);
    }
}
