using Penrose.Core.Input;

namespace Penrose.Core.Tests;

public sealed class PadMapTests
{
    [Fact]
    public void Rising_edge_maps_xbox_layout()
    {
        IReadOnlyList<ShortcutCommand> first = PadMap.Rising(PadFace.A, PadFace.None);
        Assert.Equal([ShortcutCommand.PlayPause], first);
        Assert.Empty(PadMap.Rising(PadFace.A, PadFace.A));
        Assert.Equal(
            [ShortcutCommand.SeekBackwardSmall],
            PadMap.Rising(PadFace.DPadLeft | PadFace.A, PadFace.A));
        Assert.Equal([ShortcutCommand.ToggleFullscreen], PadMap.Rising(PadFace.Menu, PadFace.None));
        Assert.Equal([ShortcutCommand.Exit], PadMap.Rising(PadFace.B, PadFace.None));
        Assert.Equal([ShortcutCommand.CycleSubtitleTrack], PadMap.Rising(PadFace.Y, PadFace.None));
    }
}
