namespace Penrose.Core.Input;

/// <summary>
/// Semantic commands. Bindings are a platform concern; this enum is shared.
/// </summary>
public enum ShortcutCommand
{
    PlayPause,
    Stop,
    SeekForwardSmall,
    SeekBackwardSmall,
    SeekForwardLarge,
    SeekBackwardLarge,
    VolumeUp,
    VolumeDown,
    ToggleMute,
    ToggleFullscreen,
    CycleAudioTrack,
    CycleSubtitleTrack,
    ToggleSubtitle,
    OpenFile,
    ShowOsc,
    Exit,
}
