using System.Reflection;

namespace MediaPlayer.App.WinUI;

internal static class AppVersion
{
    public const string Name = "Penrose";

    public static string Display
    {
        get
        {
            string? informational = typeof(App).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                int cut = informational.IndexOfAny(['+', '-']);
                return cut < 0 ? informational : informational[..cut];
            }

            return typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }
}
