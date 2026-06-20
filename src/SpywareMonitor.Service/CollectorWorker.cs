using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SpywareMonitor.Service;

public sealed class CollectorWorker : BackgroundService
{
    private readonly MonitorState _state; private readonly SettingsStore _settings; private readonly HistoryStore _history; private readonly ProcessSampler _sampler; private readonly ILogger<CollectorWorker> _logger;
    public CollectorWorker(MonitorState state, SettingsStore settings, HistoryStore history, ProcessSampler sampler, ILogger<CollectorWorker> logger) => (_state, _settings, _history, _sampler, _logger) = (state, settings, history, sampler, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        _state.SetSettings(_settings.Load());
        _history.Cleanup(_state.Settings.RetentionDays);
        while (!stoppingToken.IsCancellationRequested)
        {
            var started = DateTime.UtcNow;
            try
            {
                var (snapshot, events) = _sampler.Sample(_state.Settings);
                _state.SetLatest(snapshot);
                foreach (var item in events) _history.AddEvent(item);
                await _history.AddAsync(snapshot, _state.Settings, stoppingToken);
            }
            catch (Exception ex) { _logger.LogError(ex, "Sampling failed"); }
            var remaining = _state.Settings.SampleIntervalMs - (int)(DateTime.UtcNow - started).TotalMilliseconds;
            await Task.Delay(Math.Max(100, remaining), stoppingToken);
        }
    }
}
