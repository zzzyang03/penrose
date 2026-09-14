namespace MediaPlayer.App.WinUI;

internal static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "media-player");

    public static string Logs { get; } = Path.Combine(Root, "logs");

    public static string Database { get; } = Path.Combine(Root, "playback.db");
}
