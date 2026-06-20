using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpywareMonitor.Core;

namespace SpywareMonitor.App;

public sealed class MonitorClient
{
    public async Task<T?> SendAsync<T>(PipeRequest request, int timeoutMs = 2000)
    {
        using var timeout = new CancellationTokenSource(timeoutMs);
        await using var pipe = new NamedPipeClientStream(".", MonitorConstants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, MonitorJson.Options));
        var line = await reader.ReadLineAsync();
        var response = line is null ? null : JsonSerializer.Deserialize<PipeResponse<T>>(line, MonitorJson.Options);
        if (response?.Success != true) throw new InvalidOperationException(response?.Error ?? "The service did not respond.");
        return response.Data;
    }
}
