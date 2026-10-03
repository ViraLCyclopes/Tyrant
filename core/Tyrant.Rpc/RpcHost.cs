using Tyrant.Rpc.Data;
using Tyrant.Rpc.Jobs;
using Tyrant.Rpc.Protocol;
using Tyrant.Rpc.Studio;

namespace Tyrant.Rpc;

public static class RpcHost
{
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
}
