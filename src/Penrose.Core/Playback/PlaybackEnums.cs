namespace Penrose.Core.Playback;

public enum EngineLifecycle
{
    Created,
    Initialized,
    Disposing,
    Disposed,
    Faulted,
}

public enum MediaPhase
{
    Empty,
    Opening,
    Loaded,
    Ended,
    Failed,
}

public enum PlaybackIntent
{
    Playing,
    Paused,
}

public enum Activity
{
    None,
    Buffering,
    Seeking,
    Reconfiguring,
}

public enum EndFileReason
{
    Eof,
    Stop,
    Error,
    Redirect,
    Unknown,
}
