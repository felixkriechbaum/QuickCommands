using System.Net;
using System.Net.Http.Headers;
using System.Text;
using QuickCommands.Server.Options;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace QuickCommands.Server.Logging;

/// <summary>
/// Sends batches as JSONL to POST {Url}/api/logs?project={Project}. Serilog's batching buffers the events and retries
/// with back-off when this method throws, so the API is never slowed down or broken by an unreachable loggerui.
/// </summary>
internal sealed class LoggerUiSink : IBatchedLogEventSink, IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string _origin = Environment.MachineName;

    public LoggerUiSink(LoggerUiOptions options, HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(15);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        _endpoint = new Uri($"{options.Url.TrimEnd('/')}/api/logs?project={Uri.EscapeDataString(options.Project)}");
    }

    public async Task EmitBatchAsync(IReadOnlyCollection<LogEvent> batch)
    {
        var body = new StringBuilder();
        foreach (var e in batch)
            body.Append(LoggerUiFormatter.Format(e, _origin)).Append('\n');

        using var content = new StringContent(body.ToString(), Encoding.UTF8, "application/x-ndjson");
        using var response = await _http.PostAsync(_endpoint, content);
        if (response.IsSuccessStatusCode)
            return;

        var status = (int)response.StatusCode;
        if (status >= 500 || response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
            throw new HttpRequestException($"loggerui answered {status}; the batch will be retried.");

        // 4xx (wrong token, rejected lines): retrying would not help.
        SelfLog.WriteLine("loggerui rejected a batch of {0} events with {1}: {2}", batch.Count, status, await response.Content.ReadAsStringAsync());
    }

    public Task OnEmptyBatchAsync() => Task.CompletedTask;

    public void Dispose() => _http.Dispose();
}
