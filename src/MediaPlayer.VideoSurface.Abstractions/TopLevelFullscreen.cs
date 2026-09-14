using MediaPlayer.Core.Options;

namespace MediaPlayer.VideoSurface;

/// <summary>
/// Route A: same libmpv core, rebuild VO. PQ is allowed only here.
/// </summary>
public static class TopLevelFullscreen
{
    public const string BannerText = "已进入顶层全屏，窗口内控件暂不可用。按 Esc 返回。";

    public const string AutoHdrBannerText =
        "窗口无法输出 HDR10，已改用顶层全屏。控件暂不可用。Esc 返回。";

    /// <summary>
    /// F enters top-level only when the user allows it, the window's
    /// display is Advanced Color, and the file is HDR. <c>T</c> bypasses this.
    /// </summary>
    public static bool ShouldEnterAutomatically(
        bool allowAutomatic,
        bool displayAdvancedColor,
        bool sourceHdr) =>
        allowAutomatic && displayAdvancedColor && sourceHdr;

    public static IReadOnlyDictionary<string, string> EnterProperties(
        bool windowsHdrOn,
        DisplayColorCapabilities? display = null)
    {
        string pipeline = windowsHdrOn ? OutputPipeline.Hdr10 : OutputPipeline.Sdr;
        SurfaceBootstrapOptions surface = OutputPipeline.Apply(
            new SurfaceBootstrapOptions { D3d11OutputMode = "window" },
            pipeline,
            display);
        Dictionary<string, string> properties = new(surface.ToProperties(), StringComparer.Ordinal)
        {
            ["force-window"] = "yes",
            ["border"] = "no",
            ["ontop"] = "yes",
            ["keepaspect-window"] = "no",
            ["osc"] = "yes",
            ["osd-bar"] = "yes",
            ["input-default-bindings"] = "no",
            ["input-vo-keyboard"] = "yes",
        };
        return properties;
    }

    public static IReadOnlyDictionary<string, string> LeaveProperties(
        bool windowsHdrOn,
        bool windowFocused,
        DisplayColorCapabilities? display = null)
    {
        string pipeline = windowsHdrOn && windowFocused ? OutputPipeline.ScRgb : OutputPipeline.Sdr;
        SurfaceBootstrapOptions surface = OutputPipeline.Apply(
            new SurfaceBootstrapOptions { D3d11OutputMode = "composition" },
            pipeline,
            display);
        Dictionary<string, string> properties = new(surface.ToProperties(), StringComparer.Ordinal)
        {
            ["fullscreen"] = "no",
            ["force-window"] = "no",
            ["border"] = "yes",
            ["ontop"] = "no",
            ["keepaspect-window"] = "yes",
            ["osc"] = "no",
            ["osd-bar"] = "no",
            ["input-default-bindings"] = "no",
            ["input-vo-keyboard"] = "no",
        };
        return properties;
    }
}
