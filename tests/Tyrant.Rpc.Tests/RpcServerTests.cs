using System.Text.Json;
using Tyrant.Core.Errors;
using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.Tests;

public sealed record EchoParams(string Text, int Times = 1);

public sealed record EchoResult(string Text);

public sealed class TestMethods
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [RpcMethod("test.echo")]
    public EchoResult Echo(EchoParams p) => new(string.Concat(Enumerable.Repeat(p.Text, p.Times)));

    [RpcMethod("test.echoAsync")]
    public async Task<EchoResult> EchoAsync(EchoParams p)
    {
        await Task.Yield();
        return new EchoResult(p.Text);
    }

    [RpcMethod("test.gameMissing")]
    public EchoResult GameMissing() => throw new TyrantException(TyrantErrorCode.GameNotFound, "No game here.", FixAction.PickGameFolder);

    [RpcMethod("test.bug")]
    public EchoResult Bug() => throw new InvalidOperationException("boom");

    [RpcMethod("test.wait")]
    public async Task<EchoResult> Wait()
    {
        await _gate.Task;
        return new EchoResult("released");
    }

    [RpcMethod("test.release")]
    public EchoResult Release()
    {
        _gate.TrySetResult();
        return new EchoResult("ok");
    }
}

public class RpcServerTests
{
    private static (List<string> Raw, List<JsonElement> Lines, string Log) Run(params string[] requests)
    {
        var raw = new List<string>();
        var log = new StringWriter();
        var server = new RpcServer(line => { lock (raw) raw.Add(line); }, log).Register(new TestMethods());
        server.RunAsync(new StringReader(string.Join("\n", requests))).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        return (raw, raw.Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToList(), log.ToString());
    }

    private static JsonElement Error(JsonElement response) => response.GetProperty("error");

    [Fact]
    public void Request_gets_a_result_with_the_same_id()
    {
        var (_, lines, _) = Run("""{"jsonrpc":"2.0","id":7,"method":"test.echo","params":{"text":"hi","times":2}}""");

        var response = Assert.Single(lines);
        Assert.Equal("2.0", response.GetProperty("jsonrpc").GetString());
        Assert.Equal(7, response.GetProperty("id").GetInt32());
        Assert.Equal("hihi", response.GetProperty("result").GetProperty("text").GetString());
    }

    [Fact]
    public void String_ids_async_handlers_and_optional_params_work()
    {
        var (_, lines, _) = Run("""{"jsonrpc":"2.0","id":"a1","method":"test.echoAsync","params":{"text":"x"}}""");

        Assert.Equal("a1", lines[0].GetProperty("id").GetString());
        Assert.Equal("x", lines[0].GetProperty("result").GetProperty("text").GetString());
    }

    [Fact]
    public void Unknown_method_is_method_not_found()
    {
        var (_, lines, _) = Run("""{"jsonrpc":"2.0","id":1,"method":"nope"}""");

        Assert.Equal(RpcErrorCodes.MethodNotFound, Error(lines[0]).GetProperty("code").GetInt32());
        Assert.Contains("nope", Error(lines[0]).GetProperty("message").GetString());
    }

    [Fact]
    public void Malformed_json_is_a_parse_error_with_a_null_id()
    {
        var (_, lines, _) = Run("{not json");

        Assert.Equal(JsonValueKind.Null, lines[0].GetProperty("id").ValueKind);
        Assert.Equal(RpcErrorCodes.ParseError, Error(lines[0]).GetProperty("code").GetInt32());
    }

    [Theory]
    [InlineData("""{"id":1,"method":"test.echo"}""")]
    [InlineData("""{"jsonrpc":"1.0","id":1,"method":"test.echo"}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1}""")]
    [InlineData("[1,2]")]
    public void Malformed_requests_are_invalid_requests(string request)
    {
        var (_, lines, _) = Run(request);

        Assert.Equal(RpcErrorCodes.InvalidRequest, Error(Assert.Single(lines)).GetProperty("code").GetInt32());
    }

    [Fact]
    public void Missing_required_params_are_invalid_params()
    {
        var (_, lines, _) = Run("""{"jsonrpc":"2.0","id":1,"method":"test.echo","params":{}}""");

        Assert.Equal(RpcErrorCodes.InvalidParams, Error(lines[0]).GetProperty("code").GetInt32());
        Assert.Contains("test.echo", Error(lines[0]).GetProperty("message").GetString());
    }

    [Fact]
    public void Pk_errors_carry_their_code_and_fix()
    {
        var (_, lines, _) = Run("""{"jsonrpc":"2.0","id":1,"method":"test.gameMissing"}""");

        var error = Error(lines[0]);
        Assert.Equal(RpcErrorCodes.ToolError, error.GetProperty("code").GetInt32());
        Assert.Equal("No game here.", error.GetProperty("message").GetString());
        Assert.Equal("GAME_NOT_FOUND", error.GetProperty("data").GetProperty("code").GetString());
        Assert.Equal("PICK_GAME_FOLDER", error.GetProperty("data").GetProperty("fix").GetString());
    }

    [Fact]
    public void Unexpected_errors_are_internal_errors_and_logged_with_their_stack()
    {
        var (_, lines, log) = Run("""{"jsonrpc":"2.0","id":1,"method":"test.bug"}""");

        Assert.Equal(RpcErrorCodes.InternalError, Error(lines[0]).GetProperty("code").GetInt32());
        Assert.Contains("boom", Error(lines[0]).GetProperty("message").GetString());
        Assert.Contains("InvalidOperationException", log);
        Assert.Contains(" at ", log);
    }

    [Fact]
    public void Notifications_from_the_client_get_no_response()
    {
        var (_, lines, _) = Run("""{"jsonrpc":"2.0","method":"test.echo","params":{"text":"x"}}""");

        Assert.Empty(lines);
    }

    [Fact]
    public void A_slow_request_does_not_block_later_ones()
    {
        var (_, lines, _) = Run(
            """{"jsonrpc":"2.0","id":1,"method":"test.wait"}""",
            """{"jsonrpc":"2.0","id":2,"method":"test.release"}""");

        Assert.Equal(2, lines.Count);
        Assert.Equal("released", lines.Single(l => l.GetProperty("id").GetInt32() == 1).GetProperty("result").GetProperty("text").GetString());
    }

    [Fact]
    public void Non_ascii_text_and_newlines_stay_readable_on_one_line()
    {
        var (raw, lines, _) = Run("""{"jsonrpc":"2.0","id":1,"method":"test.echo","params":{"text":"Tricératops\nline 2"}}""");

        Assert.Contains("Tricératops", raw[0]);
        Assert.DoesNotContain('\n', raw[0]);
        Assert.Equal("Tricératops\nline 2", lines[0].GetProperty("result").GetProperty("text").GetString());
    }

    [Fact]
    public void Notify_sends_a_notification_without_an_id()
    {
        var raw = new List<string>();
        var server = new RpcServer(raw.Add, TextWriter.Null);

        server.Notify("job.progress", new { jobId = "j1", fraction = 0.5 });

        var message = JsonDocument.Parse(Assert.Single(raw)).RootElement;
        Assert.Equal("job.progress", message.GetProperty("method").GetString());
        Assert.Equal("j1", message.GetProperty("params").GetProperty("jobId").GetString());
        Assert.False(message.TryGetProperty("id", out _));
    }

    [Fact]
    public void Methods_are_listed_with_their_types()
    {
        var server = new RpcServer(_ => { }, TextWriter.Null).Register(new TestMethods());

        var echo = server.Methods.Single(m => m.Name == "test.echo");
        Assert.Equal(typeof(EchoParams), echo.ParamsType);
        Assert.Equal(typeof(EchoResult), echo.ResultType);
        Assert.Null(server.Methods.Single(m => m.Name == "test.bug").ParamsType);
        Assert.Equal(typeof(EchoResult), server.Methods.Single(m => m.Name == "test.echoAsync").ResultType);
    }
}
