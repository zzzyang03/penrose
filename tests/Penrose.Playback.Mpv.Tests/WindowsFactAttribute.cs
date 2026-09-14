namespace Penrose.Playback.Mpv.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Requires Windows and the locked libmpv build.";
        }
    }
}
