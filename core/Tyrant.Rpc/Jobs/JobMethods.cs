using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.Jobs;

public sealed class JobMethods(JobManager jobs)
{
    [RpcMethod("job.cancel")]
    public JobCancelResult Cancel(JobCancelParams p) => new(jobs.Cancel(p.JobId));
}
