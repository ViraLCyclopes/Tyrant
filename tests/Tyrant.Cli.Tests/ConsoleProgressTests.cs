using Tyrant.Cli;
using Tyrant.Core.Jobs;

namespace Tyrant.Cli.Tests;

public class ConsoleProgressTests
{
    [Fact]
    public void Thousands_of_reports_print_about_once_per_percent()
    {
        var output = new StringWriter();
        var progress = new ConsoleProgress(output, () => DateTime.UnixEpoch); // the clock never moves

        for (var i = 1; i <= 2000; i++) progress.Report(new JobProgress(i / 2000.0, $"Exported texture {i}"));

        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.InRange(lines.Length, 90, 101);
        Assert.Contains("100%", lines[^1]);
    }

    [Fact]
    public void A_slow_job_still_shows_a_line_every_second()
    {
        var output = new StringWriter();
        var now = DateTime.UnixEpoch;
        var progress = new ConsoleProgress(output, () => now);

        for (var s = 0; s < 5; s++)
        {
            progress.Report(new JobProgress(0.18, $"Waiting for the game ({s} s)"));
            now = now.AddSeconds(1);
        }

        Assert.Equal(5, output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }
}
