using Microsoft.Win32;

namespace MediaPlayer.App.WinUI;

internal static class FileAssociation
{
    private const string ProgId = "MediaPlayer.MediaFile";

    private static readonly string[] Extensions =
    [
        ".mkv", ".mp4", ".m4v", ".mov", ".webm", ".ts", ".m2ts", ".avi", ".wmv",
        ".flac", ".mp3", ".m4a", ".opus", ".strm",
    ];

    public static void RegisterCurrentUser()
    {
        string command = OpenCommand();
        using RegistryKey classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
        using (RegistryKey prog = classes.CreateSubKey(ProgId))
        {
            prog.SetValue(null, AppVersion.Name);
            using RegistryKey commandKey = prog.CreateSubKey(@"shell\open\command");
            commandKey.SetValue(null, command);
        }

        foreach (string ext in Extensions)
        {
            using RegistryKey extKey = classes.CreateSubKey(ext);
            extKey.SetValue(null, ProgId);
        }
    }

    public static void UnregisterCurrentUser()
    {
        using RegistryKey? classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true);
        classes?.DeleteSubKeyTree(ProgId, throwOnMissingSubKey: false);
        foreach (string ext in Extensions)
        {
            using RegistryKey? extKey = classes?.OpenSubKey(ext, writable: true);
            if (extKey is not null
                && string.Equals(extKey.GetValue(null) as string, ProgId, StringComparison.OrdinalIgnoreCase))
            {
                extKey.DeleteValue(string.Empty, throwOnMissingValue: false);
            }
        }
    }

    public static bool IsRegistered()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + ProgId + @"\shell\open\command");
        return key?.GetValue(null) is string;
    }

    private static string OpenCommand()
    {
        string dir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string exe = Path.Combine(dir, "MediaPlayer.App.WinUI.exe");
        // A self-contained publish carries its own runtime and can be the shell
        // verb directly. Only a framework-dependent dev build needs the .cmd that
        // points DOTNET_ROOT at the user-local SDK (and flashes a console).
        bool selfContained = File.Exists(Path.Combine(dir, "coreclr.dll"))
            || File.Exists(Path.Combine(dir, "hostfxr.dll"));
        string launcher = Path.Combine(dir, "启动.cmd");
        if (!selfContained && File.Exists(launcher))
        {
            return "cmd.exe /c \"\"" + launcher + "\" \"%1\"\"";
        }

        return "\"" + exe + "\" \"%1\"";
    }
}
