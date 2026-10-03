using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.Jobs;

public sealed class JobMethods(JobManager jobs)
{
    [RpcMethod("job.cancel")]
    public JobCancelResult Cancel(JobCancelParams p) => new(jobs.Cancel(p.JobId));

    /// <summary>The running job, so a reloaded UI can show it again and cancel it.</summary>
    [RpcMethod("job.current")]
    public JobCurrentResult Current() => new(jobs.Current());
}
