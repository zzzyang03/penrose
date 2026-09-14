using System.Runtime.InteropServices;

namespace Penrose.App.WinUI;

/// <summary>IINA SleepPreventer: keep the display awake while a file is playing.</summary>
internal static class SleepPreventer
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;

    public static void SetPlaying(bool playing)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _ = SetThreadExecutionState(
            playing
                ? EsContinuous | EsSystemRequired | EsDisplayRequired
                : EsContinuous);
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);
}
