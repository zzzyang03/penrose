using System.Text;
using Penrose.Core.Options;
using Penrose.Core.Playback;

namespace Penrose.Playback.Mpv;

internal static class LoadfileOptions
{
    /// <summary>
    /// mpv ≥ 0.38: <c>loadfile &lt;url&gt; [&lt;flags&gt; [&lt;index&gt; [&lt;options&gt;]]]</c>.
    /// The third slot is an integer playlist index; it must be -1 whenever the
    /// file-local options string is passed, otherwise mpv tries to parse the
    /// options as an integer and rejects the whole command with InvalidParameter.
    /// </summary>
    public const string NoIndex = "-1";

    public static IReadOnlyList<string> BuildCommand(PlaybackRequest request)
    {
        string options = string.Join(',', request.ToFileLocalOptions()
            .Select(pair => $"{pair.Key}={Quote(pair.Value)}"));

        string url = request.Uri.IsFile ? request.Uri.LocalPath : request.Uri.AbsoluteUri;
        List<string> args = ["loadfile", url, "replace"];
        if (options.Length > 0)
        {
            args.Add(NoIndex);
            args.Add(options);
        }

        return args;
    }

    public static void ThrowIfGlobalNetworkWrite(string propertyName)
    {
        if (OptionWhitelist.IsNetworkCredentialKey(propertyName))
        {
            throw new InvalidOperationException(
                $"Refusing to write '{propertyName}' as a global mpv property. Use loadfile file-local options.");
        }
    }

    /// <summary>
    /// mpv key-value lists only understand <c>"..."</c>, <c>[...]</c> and
    /// <c>%len%value</c> quoting; there is no backslash escape, so a bare comma
    /// (User-Agent strings, header lists) would end the value early. The length
    /// form is the only one that is safe for arbitrary content. The length is in
    /// UTF-8 bytes because that is what the C side receives.
    /// </summary>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "%" + Encoding.UTF8.GetByteCount(value).ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "%" + value;
    }
}
