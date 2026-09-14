using System.Reflection;
using System.Runtime.InteropServices;

namespace MediaPlayer.Interop.LibMpv;

public static class MpvNativeLibrary
{
    public const string WindowsFileName = "mpv-2.dll";
    public const string WindowsExportFileName = "libmpv-2.dll";

    private static readonly object Gate = new();
    private static bool _resolverRegistered;

    public static string? ResolvePath()
    {
        string? env = Environment.GetEnvironmentVariable("MPV_LIBRARY_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
        {
            return env;
        }

        if (!OperatingSystem.IsWindows())
        {
            return ExistingBesideOrRepo("libmpv.so.2");
        }

        return ExistingBesideOrRepo(WindowsFileName) ?? ExistingBesideOrRepo(WindowsExportFileName);
    }

    public static bool TryLoad(out string? path, out string? error)
    {
        RegisterDllImportResolver();
        path = ResolvePath();
        if (path is null)
        {
            error = "libmpv not found. Set MPV_LIBRARY_PATH or place the locked build under third_party/libmpv/bin/x64/.";
            return false;
        }

        try
        {
            NativeLibrary.Load(path);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void RegisterDllImportResolver()
    {
        lock (Gate)
        {
            if (_resolverRegistered)
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(typeof(MpvNativeLibrary).Assembly, Resolve);
            _resolverRegistered = true;
        }
    }

    public static uint ClientApiVersion()
    {
        if (!TryLoad(out _, out string? error))
        {
            throw new InvalidOperationException(error);
        }

        return NativeMethods.mpv_client_api_version();
    }

    public static string FormatClientApiVersion(uint packed) =>
        $"{packed >> 16}.{packed & 0xFFFF}";

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, NativeMethods.LibraryName, StringComparison.Ordinal) &&
            !string.Equals(libraryName, "mpv-2", StringComparison.Ordinal) &&
            !string.Equals(libraryName, "libmpv-2", StringComparison.Ordinal))
        {
            return 0;
        }

        string? path = ResolvePath();
        return path is null ? 0 : NativeLibrary.Load(path);
    }

    private static string? ExistingBesideOrRepo(string fileName)
    {
        string beside = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(beside))
        {
            return beside;
        }

        return FindRepoBinary(fileName);
    }

    /// <summary>
    /// Development fallback: walk up from the build output to the repository root
    /// (marked by <c>MediaPlayer.sln</c>) and use the locked build there. The walk
    /// stops at the first repo root so it can never reach a drive root where any
    /// user could plant <c>third_party\libmpv\bin\x64\mpv-2.dll</c>.
    /// </summary>
    private static string? FindRepoBinary(string fileName)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            bool repoRoot = File.Exists(Path.Combine(dir.FullName, "MediaPlayer.sln"));
            if (repoRoot)
            {
                string candidate = Path.Combine(dir.FullName, "third_party", "libmpv", "bin", "x64", fileName);
                return File.Exists(candidate) ? candidate : null;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
