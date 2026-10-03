using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace Tyrant.Rpc.Protocol;

/// <summary>A method Studio can call; listed so the TypeScript emitter can describe the protocol.</summary>
public sealed record RpcMethodInfo(string Name, Type? ParamsType, Type ResultType, Type? JobResultType);

/// <summary>
/// JSON-RPC 2.0 over newline-delimited JSON. Requests run concurrently; every outgoing line goes through
/// <c>send</c> under one lock, so responses and notifications never interleave.
/// </summary>
public sealed class RpcServer(Action<string> send, TextWriter log)
{
    private sealed record Handler(object Target, MethodInfo Method, RpcMethodInfo Info, bool TakesToken);

    private readonly Dictionary<string, Handler> _handlers = new(StringComparer.Ordinal);
    private readonly object _sendLock = new();
    private readonly CancellationTokenSource _stopping = new();

    public IReadOnlyList<RpcMethodInfo> Methods =>
        _handlers.Values.Select(h => h.Info).OrderBy(m => m.Name, StringComparer.Ordinal).ToList();

    /// <summary>Registers every public method marked [RpcMethod] on <paramref name="target"/>.</summary>
    public RpcServer Register(object target)
    {
        foreach (var method in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            var attribute = method.GetCustomAttribute<RpcMethodAttribute>();
            if (attribute is null) continue;
            var parameters = method.GetParameters();
            var takesToken = parameters.Length > 0 && parameters[^1].ParameterType == typeof(CancellationToken);
            var dataParameters = takesToken ? parameters[..^1] : parameters;
            if (dataParameters.Length > 1)
                throw new InvalidOperationException($"RPC method {attribute.Name} must take at most one parameter object.");
            var returns = method.ReturnType;
            var resultType = returns.IsGenericType && returns.GetGenericTypeDefinition() == typeof(Task<>) ? returns.GetGenericArguments()[0] : returns;
            var info = new RpcMethodInfo(attribute.Name, dataParameters.Length == 1 ? dataParameters[0].ParameterType : null, resultType, attribute.JobResult);
            if (!_handlers.TryAdd(attribute.Name, new Handler(target, method, info, takesToken)))
                throw new InvalidOperationException($"RPC method {attribute.Name} is registered twice.");
        }
        return this;
    }

    /// <summary>Sends a notification (a message without an id), e.g. job.progress.</summary>
    public void Notify(string method, object payload) =>
        Send($$"""{"jsonrpc":"2.0","method":{{JsonSerializer.Serialize(method)}},"params":{{Serialize(payload)}}}""");

    /// <summary>Reads requests until the input ends, then waits for the ones still running.</summary>
    public async Task RunAsync(TextReader input)
    {
        var running = new List<Task>();
        while (await input.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            running.RemoveAll(t => t.IsCompleted);
            running.Add(Task.Run(() => HandleAsync(line)));
        }
        _stopping.Cancel();
        await Task.WhenAll(running);
    }

    /// <summary>Handles one request line and sends its response (none for notifications).</summary>
    public async Task HandleAsync(string line)
    {
        var id = "null";
        var isNotification = false;
        try
        {
            JsonElement request;
            try
            {
                using var document = JsonDocument.Parse(line);
                request = document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new RpcException(RpcErrorCodes.ParseError, $"Parse error: {ex.Message}");
            }
            if (request.ValueKind != JsonValueKind.Object)
                throw new RpcException(RpcErrorCodes.InvalidRequest, "Invalid request: expected a JSON object.");
            if (request.TryGetProperty("id", out var idElement))
            {
                if (idElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null))
                    throw new RpcException(RpcErrorCodes.InvalidRequest, "Invalid request: id must be a string or a number.");
                id = idElement.GetRawText();
            }
            else
            {
                isNotification = true;
            }
            if (!request.TryGetProperty("jsonrpc", out var version) || version.ValueKind != JsonValueKind.String || version.GetString() != "2.0")
                throw new RpcException(RpcErrorCodes.InvalidRequest, "Invalid request: jsonrpc must be \"2.0\".");
            if (!request.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
                throw new RpcException(RpcErrorCodes.InvalidRequest, "Invalid request: method is missing.");
            var name = methodElement.GetString()!;
            if (!_handlers.TryGetValue(name, out var handler))
                throw new RpcException(RpcErrorCodes.MethodNotFound, $"Method not found: {name}.");

            var result = await InvokeAsync(handler, request.TryGetProperty("params", out var parameters) ? parameters : default);
            if (!isNotification)
                Send($$"""{"jsonrpc":"2.0","id":{{id}},"result":{{Serialize(result)}}}""");
        }
        catch (Exception ex)
        {
            var error = RpcErrors.From(ex, out var unexpected);
            if (unexpected) Log($"Unexpected error handling {Truncate(line)}: {ex}");
            if (!isNotification)
                Send($$"""{"jsonrpc":"2.0","id":{{id}},"error":{{Serialize(error)}}}""");
        }
    }

    /// <summary>Writes a timestamped line to the log (stderr in production).</summary>
    public void Log(string message)
    {
        lock (log) log.WriteLine($"{DateTime.Now:HH:mm:ss} {message}");
    }

    private async Task<object?> InvokeAsync(Handler handler, JsonElement parameters)
    {
        var args = new List<object?>();
        if (handler.Info.ParamsType is { } type)
        {
            var json = parameters.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? "{}" : parameters.GetRawText();
            try
            {
                args.Add(JsonSerializer.Deserialize(json, type, RpcJson.Options)
                    ?? throw new RpcException(RpcErrorCodes.InvalidParams, $"Invalid params for {handler.Info.Name}: expected an object."));
            }
            catch (JsonException ex)
            {
                throw new RpcException(RpcErrorCodes.InvalidParams, $"Invalid params for {handler.Info.Name}: {ex.Message}");
            }
        }
        if (handler.TakesToken) args.Add(_stopping.Token);

        object? returned;
        try
        {
            returned = handler.Method.Invoke(handler.Target, args.ToArray());
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
        if (returned is Task task)
        {
            await task;
            return handler.Info.ResultType == typeof(Task) ? null : task.GetType().GetProperty("Result")!.GetValue(task);
        }
        return returned;
    }

    private static string Serialize(object? value) =>
        value is null ? "null" : JsonSerializer.Serialize(value, value.GetType(), RpcJson.Options);

    private void Send(string line)
    {
        lock (_sendLock) send(line);
    }

    private static string Truncate(string text) => text.Length <= 200 ? text : text[..200] + "…";
}
