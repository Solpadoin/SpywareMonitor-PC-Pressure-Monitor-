using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpywareMonitor.Core;

namespace SpywareMonitor.Service;

public sealed class PipeServerWorker : BackgroundService
{
    private readonly MonitorState _state; private readonly HistoryStore _history; private readonly SettingsStore _settings; private readonly ILogger<PipeServerWorker> _logger;
    public PipeServerWorker(MonitorState state, HistoryStore history, SettingsStore settings, ILogger<PipeServerWorker> logger) => (_state, _history, _settings, _logger) = (state, history, settings, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var security = new PipeSecurity();
                security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
                security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
                var pipe = NamedPipeServerStreamAcl.Create(MonitorConstants.PipeName, PipeDirection.InOut, 5, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
                await pipe.WaitForConnectionAsync(stoppingToken);
                _ = HandleAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogError(ex, "Pipe listener failed"); await Task.Delay(500, stoppingToken); }
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using (pipe)
        using (var reader = new StreamReader(pipe, leaveOpen: true))
        using (var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true })
        {
            try
            {
                var line = await reader.ReadLineAsync();
                var request = line is null ? null : JsonSerializer.Deserialize<PipeRequest>(line, MonitorJson.Options);
                object response = request?.Command switch
                {
                    "latest" => new PipeResponse<SystemSnapshot>(true, _state.Latest),
                    "history" => new PipeResponse<IReadOnlyList<SystemSnapshot>>(true, _history.GetRecent(request.Limit)),
                    "events" => new PipeResponse<IReadOnlyList<ProcessEvent>>(true, _history.GetEvents(request.Limit)),
                    "status" => new PipeResponse<ServiceStatus>(true, new(true, _state.StartedAt, typeof(PipeServerWorker).Assembly.GetName().Version?.ToString() ?? "1.0", _state.Settings, _settings.DataDirectory, _history.StoredCount)),
                    "settings" when request.Settings is not null => SaveSettings(request.Settings),
                    _ => new PipeResponse<object>(false, null, "Unknown command")
                };
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, MonitorJson.Options));
            }
            catch (Exception ex) { try { await writer.WriteLineAsync(JsonSerializer.Serialize(new PipeResponse<object>(false, null, ex.Message), MonitorJson.Options)); } catch { } }
        }
    }

    private PipeResponse<MonitorSettings> SaveSettings(MonitorSettings settings)
    {
        var sanitized = settings with { SampleIntervalMs = Math.Clamp(settings.SampleIntervalMs, 500, 10000), RetentionDays = Math.Clamp(settings.RetentionDays, 1, 90), MaxProcesses = Math.Clamp(settings.MaxProcesses, 25, 1000) };
        _settings.Save(sanitized); _state.SetSettings(sanitized);
        return new(true, sanitized);
    }
}
