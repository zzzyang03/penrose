namespace MediaPlayer.Core.Input;

[Flags]
public enum PadFace
{
    None = 0,
    A = 1,
    B = 2,
    X = 4,
    Y = 8,
    DPadUp = 16,
    DPadDown = 32,
    DPadLeft = 64,
    DPadRight = 128,
    LeftShoulder = 256,
    RightShoulder = 512,
    Menu = 1024,
    View = 2048,
}

/// <summary>
/// Xbox-style pad → existing <see cref="ShortcutCommand"/>. Rising edge only.
/// </summary>
public static class PadMap
{
    public static IReadOnlyList<ShortcutCommand> Rising(PadFace now, PadFace was)
    {
        PadFace edge = now & ~was;
        List<ShortcutCommand> commands = [];
        Add(commands, edge, PadFace.A, ShortcutCommand.PlayPause);
        Add(commands, edge, PadFace.B, ShortcutCommand.Exit);
        Add(commands, edge, PadFace.X, ShortcutCommand.CycleAudioTrack);
        Add(commands, edge, PadFace.Y, ShortcutCommand.CycleSubtitleTrack);
        Add(commands, edge, PadFace.DPadLeft, ShortcutCommand.SeekBackwardSmall);
        Add(commands, edge, PadFace.DPadRight, ShortcutCommand.SeekForwardSmall);
        Add(commands, edge, PadFace.DPadUp, ShortcutCommand.VolumeUp);
        Add(commands, edge, PadFace.DPadDown, ShortcutCommand.VolumeDown);
        Add(commands, edge, PadFace.LeftShoulder, ShortcutCommand.SeekBackwardLarge);
        Add(commands, edge, PadFace.RightShoulder, ShortcutCommand.SeekForwardLarge);
        Add(commands, edge, PadFace.Menu, ShortcutCommand.ToggleFullscreen);
        Add(commands, edge, PadFace.View, ShortcutCommand.Exit);
        return commands;
    }

    private static void Add(List<ShortcutCommand> commands, PadFace edge, PadFace button, ShortcutCommand command)
    {
        if ((edge & button) != 0)
        {
            commands.Add(command);
        }
    }
}
