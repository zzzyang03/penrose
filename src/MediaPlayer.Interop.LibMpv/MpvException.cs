namespace MediaPlayer.Interop.LibMpv;

public sealed class MpvException : Exception
{
    public MpvException(MpvError error, string? message = null)
        : base(message ?? error.ToString())
    {
        Error = error;
    }

    public MpvError Error { get; }

    public static void ThrowIfError(int code, string action)
    {
        if (code >= 0)
        {
            return;
        }

        throw new MpvException((MpvError)code, $"{action} failed: {(MpvError)code} ({code})");
    }
}
