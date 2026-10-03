using System.Text.Json;
using Tyrant.Core.Errors;
using Tyrant.Core.Jobs;
using Tyrant.Rpc.Jobs;
using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.Tests;

public class JobManagerTests
{
    private sealed class Recorder
    {
        private readonly List<(string Method, object Payload)> _items = [];

        public void Add(string method, object payload)
        {
            lock (_items) _items.Add((method, payload));
        }

        public List<(string Method, object Payload)> Items
        {
            get { lock (_items) return [.. _items]; }
        }
    }

    private static void Idle(JobManager jobs) => Assert.True(jobs.WhenIdle().Wait(TimeSpan.FromSeconds(10)), "job did not finish");

    [Fact]
    public void Job_reports_progress_then_done_with_its_result()
    {
        var recorder = new Recorder();
        var jobs = new JobManager(recorder.Add) { ProgressInterval = TimeSpan.Zero };

        var started = jobs.Start("Test", (progress, _) =>
        {
            progress.Report(new JobProgress(0.5, "half"));
            return 42;
        });
        Idle(jobs);

        var (method, payload) = recorder.Items[0];
        Assert.Equal(JobManager.ProgressMethod, method);
        var progress = Assert.IsType<JobProgressNotification>(payload);
        Assert.Equal(started.JobId, progress.JobId);
        Assert.Equal(0.5, progress.Fraction);
        Assert.Equal("half", progress.Message);
        Assert.Equal(JobManager.DoneMethod, recorder.Items[^1].Method);
        Assert.Equal(42, Assert.IsType<JobDoneNotification>(recorder.Items[^1].Payload).Result);
        Assert.Null(jobs.RunningJobId);
    }

    [Fact]
    public void Progress_is_throttled_but_the_final_report_is_always_sent()
    {
        var recorder = new Recorder();
        var jobs = new JobManager(recorder.Add) { ProgressInterval = TimeSpan.FromHours(1) };

        jobs.Start("Busy", (progress, _) =>
        {
            for (var i = 0; i < 1000; i++) progress.Report(new JobProgress(i / 1000.0, $"step {i}"));
            progress.Report(new JobProgress(1, "done"));
            return 0;
        });
        Idle(jobs);

        var reports = recorder.Items.Where(i => i.Method == JobManager.ProgressMethod).Select(i => (JobProgressNotification)i.Payload).ToList();
        Assert.Equal(2, reports.Count);
        Assert.Equal(1, reports[^1].Fraction);
    }

    [Fact]
    public void Failures_become_job_failed_with_the_pk_error_code()
    {
        var recorder = new Recorder();
        var logged = new List<string>();
        var jobs = new JobManager(recorder.Add, logged.Add);

        jobs.Start<int>("Dump", (_, _) => throw new TyrantException(TyrantErrorCode.DumpTimeout, "No dump arrived."));
        Idle(jobs);

        var (method, payload) = recorder.Items[^1];
        Assert.Equal(JobManager.FailedMethod, method);
        var failed = Assert.IsType<JobFailedNotification>(payload);
        Assert.Equal("DUMP_TIMEOUT", failed.Error.Data!.Code);
        Assert.Equal("No dump arrived.", failed.Error.Message);
        Assert.Contains(logged, l => l.Contains("Dump") && l.Contains("failed"));
    }

    [Fact]
    public void Cancel_stops_the_job_and_reports_cancelled()
    {
        var recorder = new Recorder();
        var jobs = new JobManager(recorder.Add);
        using var entered = new ManualResetEventSlim();

        var started = jobs.Start("Long", (_, ct) =>
        {
            entered.Set();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                Thread.Sleep(5);
            }
#pragma warning disable CS0162 // unreachable: the loop only ends by cancellation
            return 0;
#pragma warning restore CS0162
        });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        Assert.True(jobs.Cancel(started.JobId));
        Idle(jobs);

        var failed = Assert.IsType<JobFailedNotification>(recorder.Items[^1].Payload);
        Assert.Equal(RpcErrorCodes.Cancelled, failed.Error.Code);
        Assert.Equal("CANCELLED", failed.Error.Data!.Code);
    }

    [Fact]
    public void Only_one_job_runs_at_a_time()
    {
        var jobs = new JobManager((_, _) => { });
        using var release = new ManualResetEventSlim();
        jobs.Start("Data dump", (_, _) => release.Wait(TimeSpan.FromSeconds(10)));

        var ex = Assert.Throws<TyrantException>(() => jobs.Start("Decompile", (_, _) => 1));
        Assert.Equal(TyrantErrorCode.JobRunning, ex.Code);
        Assert.Contains("Data dump", ex.Message);

        release.Set();
        Idle(jobs);
        jobs.Start("Decompile", (_, _) => 1);
        Idle(jobs);
    }

    [Fact]
    public void Cancel_with_an_unknown_id_returns_false()
    {
        Assert.False(new JobManager((_, _) => { }).Cancel("nope"));
    }

    [Fact]
    public void CancelAll_cancels_the_running_job()
    {
        var recorder = new Recorder();
        var jobs = new JobManager(recorder.Add);
        using var entered = new ManualResetEventSlim();
        jobs.Start("Long", (_, ct) =>
        {
            entered.Set();
            ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
            ct.ThrowIfCancellationRequested();
            return 0;
        });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        jobs.CancelAll();
        Idle(jobs);

        Assert.Equal(JobManager.FailedMethod, recorder.Items[^1].Method);
    }

    [Fact]
    public async Task Job_cancel_is_available_over_rpc()
    {
        var lines = new List<string>();
        var server = new RpcServer(lines.Add, TextWriter.Null);
        var jobs = new JobManager(server.Notify);
        server.Register(new JobMethods(jobs));
        using var release = new ManualResetEventSlim();
        var started = jobs.Start("Long", (_, ct) => { release.Wait(ct); return 0; });

        await server.HandleAsync($$$"""{"jsonrpc":"2.0","id":1,"method":"job.cancel","params":{"jobId":"{{{started.JobId}}}"}}""");
        Idle(jobs);

        var response = JsonDocument.Parse(lines.First(l => l.Contains("\"id\":1"))).RootElement;
        Assert.True(response.GetProperty("result").GetProperty("cancelled").GetBoolean());
        Assert.Contains(lines, l => l.Contains("\"job.failed\"") && l.Contains("CANCELLED"));
    }

    [Fact]
    public void A_result_that_cannot_be_sent_still_ends_the_job()
    {
        var recorder = new Recorder();
        var logged = new List<string>();
        var jobs = new JobManager((method, payload) =>
        {
            if (method == JobManager.DoneMethod) throw new InvalidOperationException("cannot serialize the result");
            recorder.Add(method, payload);
        }, logged.Add);

        jobs.Start("Bad result", (_, _) => 1);
        Idle(jobs);

        var failed = Assert.IsType<JobFailedNotification>(Assert.Single(recorder.Items).Payload);
        Assert.Equal(RpcErrorCodes.InternalError, failed.Error.Code);
        Assert.Contains(logged, l => l.Contains("cannot serialize the result"));
    }

    [Fact]
    public void Current_reports_the_running_job_and_its_latest_progress_until_it_ends()
    {
        var jobs = new JobManager((_, _) => { }) { ProgressInterval = TimeSpan.Zero };
        using var reported = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var started = jobs.Start("Decompile code", (progress, _) =>
        {
            progress.Report(new JobProgress(0.4, "Decompiling Assembly-CSharp"));
            reported.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return 1;
        });
        Assert.True(reported.Wait(TimeSpan.FromSeconds(10)));

        var current = jobs.Current();

        Assert.Equal((started.JobId, "Decompile code", 0.4, "Decompiling Assembly-CSharp"), (current!.JobId, current.Title, current.Fraction, current.Message));
        release.Set();
        Idle(jobs);
        Assert.Null(jobs.Current());
    }
}
