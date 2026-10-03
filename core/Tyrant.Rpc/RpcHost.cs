using System.Text;
using Tyrant.Rpc.Data;
using Tyrant.Rpc.Jobs;
using Tyrant.Rpc.Protocol;
using Tyrant.Rpc.Studio;

namespace Tyrant.Rpc;

public static class RpcHost
{
    private static readonly TimeSpan JobStopTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Wires the server, the job manager and every Studio method group.</summary>
    public static (RpcServer Server, JobManager Jobs) Build(StudioOptions options, Action<string> send, TextWriter log)
    {
        var server = new RpcServer(send, log);
        var session = new StudioSession(options);
        var jobs = new JobManager(server.Notify, session.Log);
        server.Register(new JobMethods(jobs));
        server.Register(new StudioMethods(session, jobs));
        server.Register(new DataMethods(session));
        return (server, jobs);
    }

    /// <summary>
    /// Serves requests from <paramref name="input"/> until it ends (Studio closed), then cancels the running job (so a dump
    /// request is withdrawn) and waits for it to clean up. Anything written to the console meanwhile goes to stderr,
    /// never into the protocol stream.
    /// </summary>
    public static async Task RunAsync(Stream input, Stream output, TextWriter log, StudioOptions options,
        Func<JobManager, IEnumerable<object>>? extraHandlers = null)
    {
        var originalOut = Console.Out;
        Console.SetOut(Console.Error);
        try
        {
            var writer = new StreamWriter(output, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
            var (server, jobs) = Build(options, writer.WriteLine, log);
            foreach (var handler in extraHandlers?.Invoke(jobs) ?? []) server.Register(handler);
            using var reader = new StreamReader(input, new UTF8Encoding(false));
            await server.RunAsync(reader);
            jobs.CancelAll();
            try
            {
                await jobs.WhenIdle().WaitAsync(JobStopTimeout);
            }
            catch (TimeoutException)
            {
                server.Log($"A running job did not stop within {JobStopTimeout.TotalSeconds:0} s of Studio closing.");
            }
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>Sidecar entry point: requests on stdin, protocol on stdout, logs on stderr — all UTF-8.</summary>
    public static Task RunConsoleAsync(StudioOptions options)
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
        }
        catch (IOException)
        {
            // No console attached (started hidden): stderr keeps its default encoding.
        }
        return RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.Error, options);
    }
}
