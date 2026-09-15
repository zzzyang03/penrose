using Penrose.Core.Capabilities;
using Penrose.Core.Playback;
using Penrose.Core.Sources;

namespace Penrose.Core.Tests;

public sealed class DeviceProfileBuilderTests
{
    [Fact]
    public void W1_profile_directplays_dolby_and_pcm_dts_not_dts_bitstream()
    {
        DeviceProfile profile = DeviceProfileBuilder.Build(DeviceProfileBuilder.ReferenceHost());
        string audio = string.Join(',', profile.DirectPlay.Select(p => p.AudioCodec));
        Assert.Contains("truehd", audio, StringComparison.Ordinal);
        Assert.Contains("eac3", audio, StringComparison.Ordinal);
        Assert.Contains("dts", audio, StringComparison.Ordinal);
        Assert.Equal(["ac3", "eac3", "truehd"], DeviceProfileBuilder.ReferenceHost().BitstreamCodecs);
        Assert.DoesNotContain("dts", DeviceProfileBuilder.ReferenceHost().BitstreamCodecs, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(profile.Subtitles, s => s is { Format: "ass", Method: "External" });
        Assert.Contains(profile.Subtitles, s => s is { Format: "pgs", Method: "Embed" });
    }

    [Fact]
    public void Truehd_omitted_when_software_decode_off_and_no_bitstream()
    {
        PlaybackCapabilitySnapshot snapshot = DeviceProfileBuilder.ReferenceHost() with
        {
            SoftwareDecodingAllowed = false,
            BitstreamAllowed = false,
            BitstreamCodecs = [],
        };
        DeviceProfile profile = DeviceProfileBuilder.Build(snapshot);
        string audio = string.Join(',', profile.DirectPlay.Select(p => p.AudioCodec));
        Assert.DoesNotContain("truehd", audio, StringComparison.Ordinal);
        Assert.DoesNotContain("dts", audio, StringComparison.Ordinal);
        Assert.DoesNotContain("av1", string.Join(',', profile.DirectPlay.Select(p => p.VideoCodec)));
        Assert.Contains("h264", string.Join(',', profile.DirectPlay.Select(p => p.VideoCodec)), StringComparison.Ordinal);
    }

    [Fact]
    public void FromRuntime_advertises_bitstream_codecs_only_with_passthrough()
    {
        PlaybackCapabilitySnapshot on = DeviceProfileBuilder.FromRuntime(
            AudioPolicy.HomeTheaterPcm, audioPassthrough: true, "wasapi/{x}", displayIsHdr: false, "d3d11va", gpuAdapterLuid: null);
        Assert.True(on.BitstreamAllowed);
        Assert.Equal(["ac3", "eac3", "dts", "truehd"], on.BitstreamCodecs);
        Assert.Equal("7.1", on.AudioLayout);

        PlaybackCapabilitySnapshot off = DeviceProfileBuilder.FromRuntime(
            AudioPolicy.ForceStereo, audioPassthrough: false, null, displayIsHdr: false, null, gpuAdapterLuid: null);
        Assert.False(off.BitstreamAllowed);
        Assert.Empty(off.BitstreamCodecs);
        Assert.Equal("stereo", off.AudioLayout);
    }

    [Fact]
    public void Playback_candidate_headers_stay_file_local()
    {
        PlaybackCandidate candidate = new()
        {
            Method = PlayMethod.DirectPlay,
            Uri = new Uri("https://example.invalid/play"),
            Headers = new Dictionary<string, string> { ["Authorization"] = "MediaBrowser Token=secret-token" },
            PlaySessionId = "sess-1",
            ItemId = "item-1",
            ProviderId = "jellyfin",
        };
        PlaybackRequest request = candidate.ToPlaybackRequest(Guid.NewGuid());
        IReadOnlyDictionary<string, string> local = request.ToFileLocalOptions();
        Assert.Contains("Authorization: MediaBrowser Token=secret-token", local["http-header-fields"]);
        Assert.DoesNotContain("secret-token", local.Keys);
        Assert.Equal("sess-1", request.ReportingContext?.PlaySessionId);
    }
}
