using MediaPlayer.Core.Playback;
using MediaPlayer.Core.Sources;

namespace MediaPlayer.Core.Tests;

public sealed class SessionReportCoordinatorTests
{
    [Fact]
    public async Task Handoff_reuses_session_without_second_start()
    {
        RecordingReporter reporter = new();
        SessionReportCoordinator coordinator = new(reporter);
        ReportingContext ctx = new("jellyfin", "item", "sess-1", null);
        coordinator.Bind("sess-1", generation: 1, reuseSession: false);
        await coordinator.StartAsync(ctx, TimeSpan.Zero, 1);
        coordinator.Bind("sess-1", generation: 2, reuseSession: true);
        await coordinator.StartAsync(ctx, TimeSpan.FromSeconds(10), 2);
        await coordinator.ProgressAsync(ctx, TimeSpan.FromSeconds(11), 2);
        await coordinator.ProgressAsync(ctx, TimeSpan.FromSeconds(11), generation: 1);
        await coordinator.StopAsync(ctx, TimeSpan.FromSeconds(12), 2);

        Assert.Equal(["start", "progress", "stop"], reporter.Calls);
        Assert.Equal(1, reporter.StartCount);
    }

    [Fact]
    public async Task Different_session_is_ignored_until_rebind()
    {
        RecordingReporter reporter = new();
        SessionReportCoordinator coordinator = new(reporter);
        coordinator.Bind("sess-1", 1, reuseSession: false);
        ReportingContext other = new("jellyfin", "item", "sess-other", null);
        await coordinator.StartAsync(other, TimeSpan.Zero, 1);
        Assert.Empty(reporter.Calls);
    }

    private sealed class RecordingReporter : IPlaybackReporter
    {
        public List<string> Calls { get; } = [];

        public int StartCount { get; private set; }

        public Task StartAsync(ReportingContext context, TimeSpan position, CancellationToken cancellationToken = default)
        {
            StartCount++;
            Calls.Add("start");
            return Task.CompletedTask;
        }

        public Task ProgressAsync(ReportingContext context, TimeSpan position, CancellationToken cancellationToken = default)
        {
            Calls.Add("progress");
            return Task.CompletedTask;
        }

        public Task PauseAsync(ReportingContext context, TimeSpan position, CancellationToken cancellationToken = default)
        {
            Calls.Add("pause");
            return Task.CompletedTask;
        }

        public Task StopAsync(ReportingContext context, TimeSpan position, CancellationToken cancellationToken = default)
        {
            Calls.Add("stop");
            return Task.CompletedTask;
        }

        public Task MarkPlayedAsync(ReportingContext context, CancellationToken cancellationToken = default)
        {
            Calls.Add("played");
            return Task.CompletedTask;
        }
    }
}
