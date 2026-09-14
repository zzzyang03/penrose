using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Penrose.App.WinUI;

internal static class ActivationPaths
{
    public static string? FromArgs(AppActivationArguments? args)
    {
        if (args is null)
        {
            return CommandLineFile();
        }

        if (args.Data is IFileActivatedEventArgs files && files.Files.Count > 0)
        {
            return files.Files[0].Path;
        }

        if (args.Data is ILaunchActivatedEventArgs launch
            && !string.IsNullOrWhiteSpace(launch.Arguments))
        {
            string trimmed = launch.Arguments.Trim().Trim('"');
            if (LooksLikeMedia(trimmed))
            {
                return trimmed;
            }

            foreach (string token in SplitArgs(launch.Arguments))
            {
                if (LooksLikeMedia(token))
                {
                    return token;
                }
            }
        }

        return CommandLineFile();
    }

    public static string? CommandLineFile()
    {
        foreach (string arg in Environment.GetCommandLineArgs().Skip(1))
        {
            if (arg.StartsWith('-'))
            {
                continue;
            }

            if (LooksLikeMedia(arg))
            {
                return arg;
            }
        }

        return null;
    }

    private static bool LooksLikeMedia(string value) =>
        File.Exists(value)
        || Directory.Exists(value)
        || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SplitArgs(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            yield break;
        }

        string current = raw.Trim().Trim('"');
        yield return current;
    }
}
