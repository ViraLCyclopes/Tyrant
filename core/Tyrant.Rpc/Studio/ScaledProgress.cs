using Tyrant.Core.Jobs;

namespace Tyrant.Rpc.Studio;

/// <summary>Maps a step's 0–1 progress into [start, end] of the whole job.</summary>
internal sealed class ScaledProgress(IProgress<JobProgress> parent, double start, double end) : IProgress<JobProgress>
{
    public void Report(JobProgress value) =>
        parent.Report(new JobProgress(start + (end - start) * Math.Clamp(value.Fraction, 0, 1), value.Message));
}
