using System.Collections.Concurrent;
using System.Text.Json;
using Tyrant.Rpc.Jobs;
using Tyrant.Rpc.Protocol;
using Tyrant.Rpc.Studio;

namespace Tyrant.Rpc.Tests;

public sealed class RpcCallException(int code, string message, string? dataCode, string? fix) : Exception(message)
{
    public int Code { get; } = code;
    public string? DataCode { get; } = dataCode;
    public string? Fix { get; } = fix;
}

/// <summary>Drives the production wiring (RpcHost.Build) in memory: send requests, await responses and job notifications.</summary>
public sealed class RpcHarness
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _responses = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _jobEnds = new();
    private readonly List<JsonElement> _notifications = [];
    private int _nextId;

    public RpcHarness(StudioOptions options)
    {
        (Server, Jobs) = RpcHost.Build(options, Receive, Log);
    }

    public StringWriter Log { get; } = new();
    public RpcServer Server { get; }
    public JobManager Jobs { get; }

    public List<JsonElement> Notifications
    {
        get { lock (_notifications) return [.. _notifications]; }
    }

    public async Task<JsonElement> Call(string method, object? parameters = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        var response = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _responses[id] = response;
        await Server.HandleAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters ?? new { } }, RpcJson.Options));
        var message = await response.Task.WaitAsync(TimeSpan.FromSeconds(30));
        if (message.TryGetProperty("error", out var error)) throw ToException(error);
        return message.GetProperty("result");
    }

    public async Task<string> StartJob(string method, object? parameters = null) =>
        (await Call(method, parameters)).GetProperty("jobId").GetString()!;

    /// <summary>Waits for job.done (returns its result) or job.failed (throws).</summary>
    public async Task<JsonElement> WaitJob(string jobId, int timeoutSeconds = 120)
    {
        var end = await JobEnd(jobId).Task.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
        var p = end.GetProperty("params");
        if (end.GetProperty("method").GetString() == JobManager.FailedMethod) throw ToException(p.GetProperty("error"));
        return p.GetProperty("result");
    }

    public async Task<JsonElement> RunJob(string method, object? parameters = null, int timeoutSeconds = 120) =>
        await WaitJob(await StartJob(method, parameters), timeoutSeconds);

    private TaskCompletionSource<JsonElement> JobEnd(string jobId) =>
        _jobEnds.GetOrAdd(jobId, _ => new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously));

    private void Receive(string line)
    {
        var message = JsonDocument.Parse(line).RootElement.Clone();
        if (message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
        {
            if (_responses.TryRemove(id.GetInt32(), out var response)) response.TrySetResult(message);
            return;
        }
        lock (_notifications) _notifications.Add(message);
        var method = message.GetProperty("method").GetString();
        if (method is JobManager.DoneMethod or JobManager.FailedMethod)
            JobEnd(message.GetProperty("params").GetProperty("jobId").GetString()!).TrySetResult(message);
    }

    private static RpcCallException ToException(JsonElement error)
    {
        string? code = null, fix = null;
        if (error.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            code = data.GetProperty("code").GetString();
            fix = data.TryGetProperty("fix", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
        }
        return new RpcCallException(error.GetProperty("code").GetInt32(), error.GetProperty("message").GetString()!, code, fix);
    }
}
