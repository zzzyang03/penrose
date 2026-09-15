using System.Text.Json;
using System.Text.Json.Serialization;
using Penrose.Core.Playback;

namespace Penrose.Core.Settings;

/// <summary>Default UI. No mpv jargon.</summary>
public sealed record SimpleSettings
{
    public string Language { get; init; } = "zh-CN";
    public HdrPolicy HdrPolicy { get; init; } = HdrPolicy.AutoTarget;
    public AudioPolicy AudioPolicy { get; init; } = AudioPolicy.SystemCompatible;
    /// <summary>Pinned mpv <c>audio-device</c>. Null leaves mpv at <c>auto</c>.</summary>
    public string? AudioDevice { get; init; }
    /// <summary>
    /// Send AC3 / E-AC3 / DTS / TrueHD to the receiver undecoded (WASAPI
    /// exclusive, mpv <c>audio-spdif</c>); other codecs still decode per
    /// <see cref="AudioPolicy"/>. Forces <see cref="NightMode"/> off while on.
    /// </summary>
    public bool AudioPassthrough { get; init; }
    public bool RememberPlaybackPosition { get; init; } = true;
    public double Volume { get; init; } = 100;
    public bool Mute { get; init; }
    public bool AutoFullscreen { get; init; }
    public bool AllowAutomaticFullscreenFallback { get; init; } = true;
    public bool NightMode { get; init; }
    public QualityPreset Quality { get; init; } = QualityPreset.Balanced;
    /// <summary>
    /// On lets mpv pick a hardware decoder (<c>hwdec=auto</c>); off decodes on the CPU
    /// (<c>hwdec=no</c>) for drivers that corrupt or crash. On by default.
    /// </summary>
    public bool HardwareDecoding { get; init; } = true;
    public double Speed { get; init; } = 1;
    public double SubDelaySeconds { get; init; }
    public string SubCodepage { get; init; } = "auto";
    public string SubAssOverride { get; init; } = "no";
    /// <summary>
    /// Pre-multi-server field, kept only so older settings files still load; the
    /// serializer moves it into <see cref="Servers"/>. Always null after loading.
    /// </summary>
    public LibraryServerSettings? Library { get; init; }
    /// <summary>Saved media servers without secrets. Tokens live in <see cref="Penrose.Core.Sources.ICredentialStore"/>.</summary>
    public IReadOnlyList<LibraryServerSettings> Servers { get; init; } = [];
    /// <summary>The server whose library is open / connected on start; one of <see cref="Servers"/>.</summary>
    public string? ActiveServerId { get; init; }

    /// <summary>The saved server that is (or should be) connected, if any.</summary>
    [JsonIgnore]
    public LibraryServerSettings? ActiveServer =>
        ActiveServerId is null ? null : Servers.FirstOrDefault(s => s.Id == ActiveServerId);
    /// <summary>Temporarily switch the window's display refresh to a clean multiple of source fps.</summary>
    public bool MatchDisplayRefresh { get; init; }
    /// <summary>Turn Windows HDR on for PQ/HLG sources, then restore.</summary>
    public bool AutoEnableWindowsHdr { get; init; }
    public bool SeekThumbnails { get; init; } = true;
    public bool GamepadEnabled { get; init; } = true;
    /// <summary>
    /// Let Emby/Jellyfin <c>Protocol=File</c> paths (UNC shares, local drives) be
    /// opened directly. Off by default: a server-supplied UNC path makes this
    /// machine open an SMB session to whatever host the server names.
    /// </summary>
    public bool AllowServerFilePaths { get; init; }
    /// <summary>
    /// mpv <c>audio-channels</c> override chosen from the transport bar (stereo,
    /// 2.1, 5.1, 7.1, or "auto" for the source layout). Empty follows the audio policy.
    /// Only shapes locally decoded tracks; a bitstreamed track is laid out by the receiver.
    /// </summary>
    public string AudioChannelsOverride { get; init; } = "";
    /// <summary>Keep the thin progress line visible in fullscreen after the controls hide.</summary>
    public bool FullscreenProgressLine { get; init; }
}

/// <summary>Emby/Jellyfin account pointer. Password and token are not stored here.</summary>
public sealed record LibraryServerSettings
{
    /// <summary>Stable key for the token store and the active-server pointer.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    /// <summary>Display name chosen by the user; falls back to the host.</summary>
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "emby";
    public string BaseUrl { get; init; } = "";
    public string UserName { get; init; } = "";
    public string? UserId { get; init; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Host : Name;

    [JsonIgnore]
    public string Host => Uri.TryCreate(BaseUrl, UriKind.Absolute, out Uri? url) ? url.Host : BaseUrl;
}

/// <summary>Visible after the advanced toggle. Still not a raw mpv.conf dump.</summary>
public sealed record AdvancedSettings
{
    public string? Hwdec { get; init; } = "auto";
    public bool D3d11vaZeroCopy { get; init; }
    public string ToneMapping { get; init; } = "auto";
    public string? SubHdrPeak { get; init; }
    public string? ImageSubsHdrPeak { get; init; }
    public string BlendSubtitles { get; init; } = "no";
    public string AudioChannels { get; init; } = "auto-safe";
    public bool AudioExclusive { get; init; }
    public string? AudioSpdif { get; init; }
    public double SubDelaySeconds { get; init; }
    public bool DualSubtitles { get; init; }
}

public static class SimpleSettingsSerializer
{
    public const string StoreKey = "simple";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Unknown enum names (downgrade, hand edit) fall back to the default member
        // instead of throwing and wiping every other setting with them.
        Converters = { new LenientEnumConverterFactory() },
    };

    public static string ToJson(SimpleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return JsonSerializer.Serialize(settings, Options);
    }

    public static SimpleSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SimpleSettings();
        }

        try
        {
            SimpleSettings loaded = JsonSerializer.Deserialize<SimpleSettings>(json, Options) ?? new SimpleSettings();
            if (HasLegacyBitstreamPolicy(json))
            {
                loaded = loaded with { AudioPolicy = AudioPolicy.HomeTheaterPcm, AudioPassthrough = true };
            }

            return Migrate(loaded);
        }
        catch (JsonException)
        {
            return new SimpleSettings();
        }
    }

    /// <summary>
    /// Up to 0.1.1 passthrough was a fourth <c>audioPolicy</c> value, <c>Bitstream</c>.
    /// The lenient enum converter turns that unknown name into the default, so the
    /// raw document is checked before the value is lost.
    /// </summary>
    private static bool HasLegacyBitstreamPolicy(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("audioPolicy", out JsonElement policy)
            && policy.ValueKind == JsonValueKind.String
            && string.Equals(policy.GetString(), "Bitstream", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Single <c>library</c> entry (pre multi-server) becomes the first, active saved server.</summary>
    public static SimpleSettings Migrate(SimpleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Library is null)
        {
            return settings;
        }

        LibraryServerSettings legacy = settings.Library;
        if (settings.Servers.Count > 0 || string.IsNullOrWhiteSpace(legacy.BaseUrl))
        {
            return settings with { Library = null };
        }

        LibraryServerSettings migrated = legacy with { Name = legacy.DisplayName };
        return settings with
        {
            Library = null,
            Servers = [migrated],
            ActiveServerId = migrated.Id,
        };
    }
}

/// <summary>
/// String enums that tolerate unknown names: they deserialize to the enum's
/// default value (0) rather than failing the whole document.
/// </summary>
public sealed class LenientEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return typeToConvert.IsEnum;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        Type converter = typeof(LenientEnumConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converter)!;
    }

    private sealed class LenientEnumConverter<T> : JsonConverter<T>
        where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    string? name = reader.GetString();
                    return !string.IsNullOrWhiteSpace(name) && Enum.TryParse(name, ignoreCase: true, out T parsed)
                        && Enum.IsDefined(parsed)
                        ? parsed
                        : default;
                case JsonTokenType.Number when reader.TryGetInt32(out int number):
                    T numeric = (T)Enum.ToObject(typeof(T), number);
                    return Enum.IsDefined(numeric) ? numeric : default;
                default:
                    return default;
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
