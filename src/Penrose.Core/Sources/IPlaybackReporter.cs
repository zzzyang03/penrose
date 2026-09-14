using Penrose.Core.Playback;

namespace Penrose.Core.Sources;

public interface IPlaybackReporter
{
    Task StartAsync(ReportingContext context, TimeSpan position, CancellationToken cancellationToken = default);

    Task ProgressAsync(ReportingContext context, TimeSpan position, CancellationToken cancellationToken = default);

    Task PauseAsync(ReportingContext context, TimeSpan position, CancellationToken cancellationToken = default);

    Task StopAsync(ReportingContext context, TimeSpan position, CancellationToken cancellationToken = default);

    Task MarkPlayedAsync(ReportingContext context, CancellationToken cancellationToken = default);
}
