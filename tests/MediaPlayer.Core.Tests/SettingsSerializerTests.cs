using MediaPlayer.Core.Playback;
using MediaPlayer.Core.Settings;

namespace MediaPlayer.Core.Tests;

public sealed class SettingsSerializerTests
{
    [Fact]
    public void Unknown_enum_name_falls_back_without_discarding_other_settings()
    {
        SimpleSettings settings = SimpleSettingsSerializer.FromJson(
            """
            {
              "language": "en",
              "audioPolicy": "SomethingFromTheFuture",
              "quality": "High",
              "volume": 42,
              "library": { "kind": "emby", "baseUrl": "https://emby.example/", "userName": "u", "userId": "id" }
            }
            """);

        Assert.Equal("en", settings.Language);
        Assert.Equal(AudioPolicy.SystemCompatible, settings.AudioPolicy);
        Assert.Equal(QualityPreset.High, settings.Quality);
        Assert.Equal(42, settings.Volume);
        // The single pre-multi-server entry becomes the first, active saved server.
        Assert.Null(settings.Library);
        LibraryServerSettings server = Assert.Single(settings.Servers);
        Assert.Equal("id", server.UserId);
        Assert.Equal("emby.example", server.Name);
        Assert.Equal(server.Id, settings.ActiveServerId);
        Assert.Same(server, settings.ActiveServer);
    }

    [Fact]
    public void Servers_round_trip_and_computed_fields_stay_out_of_json()
    {
        LibraryServerSettings a = new() { Name = "Home", BaseUrl = "https://emby.example:8920/", UserName = "u", UserId = "1" };
        LibraryServerSettings b = new() { BaseUrl = "http://nas.local:8096/", UserName = "v", UserId = "2" };
        SimpleSettings original = new() { Servers = [a, b], ActiveServerId = b.Id };
        string json = SimpleSettingsSerializer.ToJson(original);
        Assert.DoesNotContain("activeServer\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("displayName", json, StringComparison.Ordinal);
        SimpleSettings back = SimpleSettingsSerializer.FromJson(json);
        Assert.Equal(2, back.Servers.Count);
        Assert.Equal("Home", back.Servers[0].DisplayName);
        Assert.Equal("nas.local", back.Servers[1].DisplayName);
        Assert.Equal(b.Id, back.ActiveServer?.Id);
    }

    [Fact]
    public void Round_trip_keeps_enums_as_names()
    {
        SimpleSettings original = new() { AudioPolicy = AudioPolicy.Bitstream, Quality = QualityPreset.Fast, AllowServerFilePaths = true };
        string json = SimpleSettingsSerializer.ToJson(original);
        Assert.Contains("\"audioPolicy\":\"Bitstream\"", json, StringComparison.Ordinal);
        SimpleSettings back = SimpleSettingsSerializer.FromJson(json);
        Assert.Equal(AudioPolicy.Bitstream, back.AudioPolicy);
        Assert.Equal(QualityPreset.Fast, back.Quality);
        Assert.True(back.AllowServerFilePaths);
    }

    [Fact]
    public void Server_file_paths_are_off_by_default()
    {
        Assert.False(new SimpleSettings().AllowServerFilePaths);
        Assert.False(SimpleSettingsSerializer.FromJson("{}").AllowServerFilePaths);
    }
}
