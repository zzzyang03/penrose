namespace Penrose.Core.Options;

public sealed record MpvConfParseResult(
    IReadOnlyDictionary<string, string> Accepted,
    IReadOnlyList<OptionValidationResult> Rejected);

/// <summary>
/// User-owned mpv.conf. Structural Engine/Surface keys are rejected; PlaybackPolicy
/// and UserAdvanced keys apply. Unknown keys are listed, never silently ignored.
/// Credential / TLS / script keys are rejected before whitelist lookup so they can
/// never be applied half-way. Handles <c>--key</c>, <c>no-key</c>, quoted values,
/// <c>[profile]</c> sections (skipped) and trailing <c># comments</c>.
/// </summary>
public static class MpvConfParser
{
    public static MpvConfParseResult Parse(string? text)
    {
        Dictionary<string, string> accepted = new(StringComparer.Ordinal);
        List<OptionValidationResult> rejected = [];
        if (string.IsNullOrWhiteSpace(text))
        {
            return new MpvConfParseResult(accepted, rejected);
        }

        bool inProfile = false;
        foreach (string raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                // Conditional/named profiles are not applied; only [default] returns to the top level.
                string section = line[1..^1].Trim();
                inProfile = !section.Equals("default", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inProfile)
            {
                continue;
            }

            if (line.StartsWith("--", StringComparison.Ordinal))
            {
                line = line[2..];
            }

            int split = line.IndexOf('=');
            if (split < 0)
            {
                split = line.IndexOf(' ');
            }

            string key = (split < 0 ? line : line[..split]).Trim();
            string value = split < 0 ? "yes" : ReadValue(line[(split + 1)..]);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (split < 0 && key.StartsWith("no-", StringComparison.Ordinal) && key.Length > 3)
            {
                key = key[3..];
                value = "no";
            }

            if (OptionWhitelist.IsNetworkCredentialKey(key) || OptionWhitelist.IsSecuritySensitiveKey(key))
            {
                rejected.Add(OptionValidationResult.Reject(
                    key,
                    OptionWhitelist.IsNetworkCredentialKey(key)
                        ? $"Option '{key}' is a network credential; it is file-local and never read from mpv.conf."
                        : $"Option '{key}' affects transport security or runs external programs; not user-settable."));
                continue;
            }

            OptionValidationResult advanced = OptionWhitelist.Validate(OptionLayer.UserAdvanced, key);
            OptionValidationResult policy = OptionWhitelist.Validate(OptionLayer.PlaybackPolicy, key);
            if (!advanced.Accepted && !policy.Accepted)
            {
                rejected.Add(advanced);
                continue;
            }

            string? valueProblem = ValidateValue(key, value);
            if (valueProblem is not null)
            {
                rejected.Add(OptionValidationResult.Reject(key, valueProblem));
                continue;
            }

            accepted[key] = value;
        }

        return new MpvConfParseResult(accepted, rejected);
    }

    /// <summary>
    /// A quoted value runs to the closing quote; an unquoted value ends at the
    /// first whitespace-preceded '#', which mpv treats as a trailing comment.
    /// </summary>
    private static string ReadValue(string rest)
    {
        string trimmed = rest.Trim();
        if (trimmed.Length >= 2 && (trimmed[0] == '"' || trimmed[0] == '\''))
        {
            char quote = trimmed[0];
            int close = trimmed.IndexOf(quote, 1);
            if (close > 0)
            {
                return trimmed[1..close];
            }

            return trimmed[1..];
        }

        for (int i = 1; i < trimmed.Length; i++)
        {
            if (trimmed[i] == '#' && char.IsWhiteSpace(trimmed[i - 1]))
            {
                return trimmed[..i].TrimEnd();
            }
        }

        return trimmed;
    }

    /// <summary>
    /// Whitelisted keys whose <em>values</em> can still reach the filesystem or
    /// spawn work outside playback: lavfi graphs can write files, <c>ao=pcm</c>
    /// dumps audio to disk.
    /// </summary>
    private static string? ValidateValue(string key, string value)
    {
        switch (key)
        {
            case "af":
            case "vf":
                if (value.Contains("lavfi", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("file=", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Option '{key}' may not use lavfi graphs or file outputs from mpv.conf.";
                }

                break;
            case "ao":
                if (value.Contains("pcm", StringComparison.OrdinalIgnoreCase))
                {
                    return "Option 'ao' may not select the pcm (file dump) output.";
                }

                break;
        }

        return null;
    }
}
