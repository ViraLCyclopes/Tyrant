using Tyrant.Core.Errors;
using Tyrant.Core.Jobs;
using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.Jobs;

public sealed record JobStarted(string JobId);

public sealed record JobCancelParams(string JobId);

public sealed record JobCancelResult(bool Cancelled);

public sealed record JobProgressNotification(string JobId, double Fraction, string Message);

public sealed record JobDoneNotification(string JobId, object? Result);

public sealed record JobFailedNotification(string JobId, RpcErrorObject Error);

/// <summary>
/// Runs one long operation at a time in the background. The caller gets a job id at once; progress and the result
/// or error follow as job.progress / job.done / job.failed notifications.
/// </summary>
public sealed class JobManager(Action<string, object> notify, Action<string>? log = null)
{
    public const string ProgressMethod = "job.progress";
    public const string DoneMethod = "job.done";
    public const string FailedMethod = "job.failed";

    private sealed class RunningJob(string id, string title)
    {
        public string Id { get; } = id;
        public string Title { get; } = title;
        public CancellationTokenSource Cancellation { get; } = new();
        public Task Task { get; set; } = Task.CompletedTask;
    }

    private readonly Action<string, object> _notify = notify;
    private readonly object _lock = new();
    private RunningJob? _running;
    private Task _lastJob = Task.CompletedTask; // completes only after the job's final notification is sent

    /// <summary>Minimum time between two progress notifications; the final 100 % report is always sent.</summary>
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    public string? RunningJobId
    {
        get { lock (_lock) return _running?.Id; }
    }

    public JobStarted Start<T>(string title, Func<IProgress<JobProgress>, CancellationToken, T> work)
    {
        Task task;
        RunningJob job;
        lock (_lock)
        {
            if (_running is not null)
                throw new TyrantException(TyrantErrorCode.JobRunning, $"'{_running.Title}' is still running; wait for it to finish or cancel it first.");
            job = new RunningJob(Guid.NewGuid().ToString("N")[..12], title);
            task = new Task(() => Execute(job, work), TaskCreationOptions.LongRunning);
            job.Task = task;
            _running = job;
            _lastJob = task;
        }
        task.Start();
        return new JobStarted(job.Id);
    }

    public bool Cancel(string jobId)
    {
        lock (_lock)
        {
            if (_running is null || _running.Id != jobId) return false;
            _running.Cancellation.Cancel();
            return true;
        }
    }

    public void CancelAll()
    {
        lock (_lock) _running?.Cancellation.Cancel();
    }

    /// <summary>Completes when no job is running and the last one's final notification has been sent.</summary>
    public Task WhenIdle()
    {
        lock (_lock) return _lastJob;
    }

    private void Execute<T>(RunningJob job, Func<IProgress<JobProgress>, CancellationToken, T> work)
    {
        log?.Invoke($"Job '{job.Title}' ({job.Id}) started.");
        string method;
        object notification;
        try
        {
            var result = work(new ThrottledProgress(this, job.Id), job.Cancellation.Token);
            (method, notification) = (DoneMethod, new JobDoneNotification(job.Id, result));
            log?.Invoke($"Job '{job.Title}' ({job.Id}) finished.");
        }
        catch (Exception ex)
        {
            var error = RpcErrors.From(ex, out var unexpected);
            (method, notification) = (FailedMethod, new JobFailedNotification(job.Id, error));
            log?.Invoke($"Job '{job.Title}' ({job.Id}) failed: {(unexpected ? ex.ToString() : error.Message)}");
        }
        finally
        {
            // Cleared before the final notification, so Studio can start the next job as soon as it hears about this one.
            lock (_lock) _running = null;
            job.Cancellation.Dispose();
        }
        try
        {
            _notify(method, notification);
        }
        catch (Exception ex) when (method == DoneMethod)
        {
            // The result could not be sent (e.g. it failed to serialize): end the job anyway, or Studio would wait forever.
            log?.Invoke($"Job '{job.Title}' ({job.Id}) finished but its result could not be sent: {ex}");
            _notify(FailedMethod, new JobFailedNotification(job.Id,
                new RpcErrorObject(RpcErrorCodes.InternalError, $"The result could not be sent ({ex.GetType().Name}): {ex.Message}", null)));
        }
    }

    private sealed class ThrottledProgress(JobManager owner, string jobId) : IProgress<JobProgress>
    {
        private long _lastTicks = long.MinValue;

        public void Report(JobProgress value)
        {
            var now = Environment.TickCount64;
            var due = _lastTicks == long.MinValue || now - _lastTicks >= (long)owner.ProgressInterval.TotalMilliseconds;
            if (value.Fraction < 1 && !due) return;
            _lastTicks = now;
            owner._notify(ProgressMethod, new JobProgressNotification(jobId, Math.Clamp(value.Fraction, 0, 1), value.Message));
        }
    }
}
