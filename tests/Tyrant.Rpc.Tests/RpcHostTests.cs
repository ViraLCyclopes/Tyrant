using System.Text;
using System.Text.Json;
using Tyrant.Rpc.Jobs;
using Tyrant.Rpc.Protocol;

[assembly: CollectionBehavior(DisableTestParallelization = true)] // RpcHost redirects the process-wide Console

namespace Tyrant.Rpc.Tests;

public sealed class NoisyMethods
{
    [RpcMethod("test.noisy")]
    public EchoResult Noisy()
    {
        Console.WriteLine("noise from a library");
        return new EchoResult("ok");
    }
}

public sealed class SlowJobMethods(JobManager jobs)
{
    [RpcMethod("test.slowJob")]
    public JobStarted Slow() => jobs.Start("Slow", (_, ct) =>
    {
        ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
        ct.ThrowIfCancellationRequested();
        return 0;
    });
}

public class RpcHostTests
{
    private static List<string> Serve(string input, Func<JobManager, IEnumerable<object>>? handlers = null)
    {
        using var stdin = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var stdout = new MemoryStream();
        RpcHost.RunAsync(stdin, stdout, TextWriter.Null, TestStudio.Options(), handlers).WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
        return Encoding.UTF8.GetString(stdout.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    [Fact]
    public void Serves_requests_until_the_input_ends()
    {
        var lines = Serve("""{"jsonrpc":"2.0","id":1,"method":"app.info"}""" + "\n");

        var response = JsonDocument.Parse(Assert.Single(lines)).RootElement;
        Assert.Equal(1, response.GetProperty("result").GetProperty("protocolVersion").GetInt32());
    }

    [Fact]
    public void Console_output_from_handlers_never_reaches_the_protocol()
    {
        var (originalOut, originalError) = (Console.Out, Console.Error);
        var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            var lines = Serve("""{"jsonrpc":"2.0","id":1,"method":"test.noisy"}""" + "\n", _ => [new NoisyMethods()]);

            var response = JsonDocument.Parse(Assert.Single(lines)).RootElement;
            Assert.Equal("ok", response.GetProperty("result").GetProperty("text").GetString());
            Assert.Contains("noise from a library", stderr.ToString());
            Assert.Same(originalOut, Console.Out);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void End_of_input_cancels_the_running_job()
    {
        var lines = Serve("""{"jsonrpc":"2.0","id":1,"method":"test.slowJob"}""" + "\n", jobs => [new SlowJobMethods(jobs)]);

        Assert.Contains(lines, l => l.Contains("\"jobId\"") && l.Contains("\"id\":1"));
        var failed = JsonDocument.Parse(lines.Single(l => l.Contains("\"job.failed\""))).RootElement;
        Assert.Equal("CANCELLED", failed.GetProperty("params").GetProperty("error").GetProperty("data").GetProperty("code").GetString());
    }
}
