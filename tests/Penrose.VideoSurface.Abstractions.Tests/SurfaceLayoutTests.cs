using Penrose.VideoSurface;

namespace Penrose.VideoSurface.Abstractions.Tests;

public sealed class SurfaceLayoutTests
{
    [Fact]
    public void Pixel_size_is_dip_times_composition_scale()
    {
        SurfaceLayout layout = new(800, 450, 1.5, 1.5, 1.5);
        Assert.Equal(1200, layout.PixelWidth);
        Assert.Equal(675, layout.PixelHeight);
        Assert.Equal("1200x675", layout.CompositionSize);
    }

    [Fact]
    public void Rasterization_scale_does_not_replace_composition_scale()
    {
        SurfaceLayout layout = new(100, 100, 2.0, 2.0, 1.25);
        Assert.Equal(200, layout.PixelWidth);
        Assert.Equal(200, layout.PixelHeight);
    }

    [Fact]
    public void Stale_generation_is_dropped()
    {
        Assert.False(SwapChainOwnership.ShouldApply(eventGeneration: 3, currentGeneration: 4));
        Assert.True(SwapChainOwnership.ShouldApply(eventGeneration: 4, currentGeneration: 4));
        Assert.False(SwapChainOwnership.IsLiveAddress(0));
    }

    [Fact]
    public void Interactive_move_skips_non_forced_bind_and_display_refresh()
    {
        Assert.True(SurfaceBindGate.ShouldSkipBind(interactiveMove: true, force: false));
        Assert.False(SurfaceBindGate.ShouldSkipBind(interactiveMove: true, force: true));
        Assert.False(SurfaceBindGate.ShouldSkipBind(interactiveMove: false, force: false));
        Assert.True(SurfaceBindGate.ShouldSkipDisplayRefresh(interactiveMove: true));
        Assert.False(SurfaceBindGate.ShouldSkipDisplayRefresh(interactiveMove: false));
    }

    [Fact]
    public void Hdr10_pipeline_maps_peak_prim_trc_gamut()
    {
        DisplayColorCapabilities caps = new(
            AdapterLuid: 42,
            ActiveColorMode: ActiveColorMode.Hdr,
            OutputColorSpace: "pq",
            MaxLuminance: 600,
            MinLuminance: 0.001,
            MaxFullFrameLuminance: 400,
            ContainerPrimaries: "bt.2020",
            EffectiveGamut: "dci-p3",
            SdrWhiteLevel: 203);

        IReadOnlyDictionary<string, string> options = caps.ToTargetOptions("hdr10");
        Assert.Equal("600", options["target-peak"]);
        Assert.Equal("bt.2020", options["target-prim"]);
        Assert.Equal("pq", options["target-trc"]);
        Assert.Equal("dci-p3", options["target-gamut"]);
    }

    [Fact]
    public void Implausible_getdesc1_peak_falls_back_to_full_frame_then_400()
    {
        DisplayColorCapabilities laptop = new(
            AdapterLuid: 1,
            ActiveColorMode: ActiveColorMode.Hdr,
            OutputColorSpace: "pq",
            MaxLuminance: 7303,
            MinLuminance: 0.001,
            MaxFullFrameLuminance: 474,
            ContainerPrimaries: null,
            EffectiveGamut: null,
            SdrWhiteLevel: 80);
        Assert.Equal(474, laptop.SanitizedPeakNits());
        Assert.Equal("474", laptop.ToTargetOptions(OutputPipeline.Hdr10)["target-peak"]);
        Assert.Equal("203", laptop.ToTargetOptions(OutputPipeline.Sdr)["target-peak"]);

        DisplayColorCapabilities garbage = laptop with { MaxFullFrameLuminance = 7303 };
        Assert.Equal(400, garbage.SanitizedPeakNits());

        DisplayColorCapabilities external = laptop with { MaxLuminance = 1435.8776, MaxFullFrameLuminance = 1435.8776 };
        Assert.Equal(1436, external.SanitizedPeakNits());
    }

    [Fact]
    public void Named_pipelines_set_format_and_csp()
    {
        Penrose.Core.Options.SurfaceBootstrapOptions empty = new();
        Penrose.Core.Options.SurfaceBootstrapOptions scrgb = OutputPipeline.Apply(empty, OutputPipeline.ScRgb);
        Assert.Equal("rgba16f", scrgb.D3d11OutputFormat);
        Assert.Equal("linear", scrgb.D3d11OutputCsp);
        Penrose.Core.Options.SurfaceBootstrapOptions pq = OutputPipeline.Apply(empty, OutputPipeline.Hdr10);
        Assert.Equal("rgb10_a2", pq.D3d11OutputFormat);
        Assert.Equal("pq", pq.D3d11OutputCsp);
        Penrose.Core.Options.SurfaceBootstrapOptions sdr = OutputPipeline.Apply(empty, OutputPipeline.Sdr);
        Assert.Equal("rgba8", sdr.D3d11OutputFormat);
        Assert.Equal("srgb", sdr.D3d11OutputCsp);
        Assert.Equal("no", sdr.TargetColorspaceHint);
        Assert.Equal("203", sdr.TargetPeak);
        Assert.Equal("400", scrgb.TargetPeak);
        Assert.Equal("400", pq.TargetPeak);

        DisplayColorCapabilities display = new(
            AdapterLuid: 7,
            ActiveColorMode: ActiveColorMode.Hdr,
            OutputColorSpace: "pq",
            MaxLuminance: 1436,
            MinLuminance: 0.002,
            MaxFullFrameLuminance: 800,
            ContainerPrimaries: null,
            EffectiveGamut: "dci-p3",
            SdrWhiteLevel: 80);
        Penrose.Core.Options.SurfaceBootstrapOptions injected = OutputPipeline.Apply(empty, OutputPipeline.ScRgb, display);
        Assert.Equal("1436", injected.TargetPeak);
        Assert.Equal("dci-p3", injected.TargetGamut);
        Penrose.Core.Options.SurfaceBootstrapOptions sdrOnHdrDesktop = OutputPipeline.Apply(empty, OutputPipeline.Sdr, display);
        Assert.Equal("203", sdrOnHdrDesktop.TargetPeak);
    }

    [Fact]
    public void Route_a_enter_uses_window_mode_and_pq_when_hdr_on()
    {
        IReadOnlyDictionary<string, string> enter = TopLevelFullscreen.EnterProperties(windowsHdrOn: true);
        Assert.Equal("window", enter["d3d11-output-mode"]);
        Assert.Equal("rgb10_a2", enter["d3d11-output-format"]);
        Assert.Equal("pq", enter["d3d11-output-csp"]);
        Assert.False(enter.ContainsKey("fullscreen"));
        Assert.Equal("yes", enter["force-window"]);
        Assert.Equal("no", enter["border"]);
        Assert.Equal("no", enter["keepaspect-window"]);
        Assert.Equal("no", enter["input-default-bindings"]);
        Assert.Equal("yes", enter["osc"]);
        Assert.Equal("400", enter["target-peak"]);
    }

    [Fact]
    public void Route_a_leave_restores_composition_scrgb_when_windows_hdr_on()
    {
        IReadOnlyDictionary<string, string> leave = TopLevelFullscreen.LeaveProperties(windowsHdrOn: true);
        Assert.Equal("composition", leave["d3d11-output-mode"]);
        Assert.Equal("rgba16f", leave["d3d11-output-format"]);
        Assert.Equal("linear", leave["d3d11-output-csp"]);
        Assert.Equal("no", leave["fullscreen"]);
        Assert.Equal("no", leave["force-window"]);
        Assert.Equal("yes", leave["keepaspect-window"]);
        Assert.Equal("no", leave["osc"]);

        IReadOnlyDictionary<string, string> sdr = TopLevelFullscreen.LeaveProperties(windowsHdrOn: false);
        Assert.Equal("composition", sdr["d3d11-output-mode"]);
        Assert.Equal("rgba8", sdr["d3d11-output-format"]);
        Assert.Equal("srgb", sdr["d3d11-output-csp"]);
    }

    [Fact]
    public void Windowed_pipeline_follows_display_only()
    {
        Assert.Equal(OutputPipeline.ScRgb, OutputPipeline.Windowed(displayAdvancedColor: true));
        Assert.Equal(OutputPipeline.Sdr, OutputPipeline.Windowed(displayAdvancedColor: false));
    }

    [Fact]
    public void Automatic_toplevel_needs_setting_hdr_display_and_hdr_source()
    {
        Assert.True(TopLevelFullscreen.ShouldEnterAutomatically(
            allowAutomatic: true,
            displayAdvancedColor: true,
            sourceHdr: true));
        Assert.False(TopLevelFullscreen.ShouldEnterAutomatically(
            allowAutomatic: false,
            displayAdvancedColor: true,
            sourceHdr: true));
        Assert.False(TopLevelFullscreen.ShouldEnterAutomatically(
            allowAutomatic: true,
            displayAdvancedColor: false,
            sourceHdr: true));
        Assert.False(TopLevelFullscreen.ShouldEnterAutomatically(
            allowAutomatic: true,
            displayAdvancedColor: true,
            sourceHdr: false));
    }
}
