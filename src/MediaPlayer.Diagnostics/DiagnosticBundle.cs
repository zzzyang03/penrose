namespace MediaPlayer.Diagnostics;

/// <summary>
/// Diagnostic packages are never uploaded automatically. The user must export.
/// </summary>
public interface IDiagnosticBundleExporter
{
    Task<string> ExportAsync(CancellationToken cancellationToken = default);
}

public sealed class FileDiagnosticBundleExporter : IDiagnosticBundleExporter
{
    /// <summary>Bundles live in their own folder so an export never re-ingests earlier exports.</summary>
    public const string BundleFolderName = "bundles";

    private readonly string _logDirectory;

    public FileDiagnosticBundleExporter(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        _logDirectory = logDirectory;
    }

    public async Task<string> ExportAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_logDirectory);
        string bundleDirectory = Path.Combine(_logDirectory, BundleFolderName);
        Directory.CreateDirectory(bundleDirectory);
        string dest = Path.Combine(
            bundleDirectory,
            "diagnostics-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".txt");
        string[] files = Directory.GetFiles(_logDirectory, "*.log", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        await using StreamWriter writer = new(dest);
        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync("===== " + Path.GetFileName(file) + " =====").ConfigureAwait(false);
            string content;
            try
            {
                // The active log is held open by the sink; share the read.
                await using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using StreamReader reader = new(stream);
                content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                content = "(unreadable: " + ex.Message + ")";
            }

            await writer.WriteLineAsync(LogRedactor.Redact(content)).ConfigureAwait(false);
        }

        return dest;
    }
}
