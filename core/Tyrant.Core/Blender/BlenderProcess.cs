using System.Diagnostics;

namespace Tyrant.Core.Blender;

public sealed record BlenderRun(int ExitCode, string Output);

/// <summary>Runs blender.exe; tests use a fake so nothing is ever started.</summary>
public interface IBlenderProcess
{
    /// <summary>Runs Blender to the end (or the timeout, then kills it) and returns its exit code and stdout+stderr.</summary>
    BlenderRun Run(string exe, IReadOnlyList<string> args, IReadOnlyDictionary<string, string>? env = null, TimeSpan? timeout = null);

    /// <summary>Starts Blender for the user and returns at once.</summary>
    void Start(string exe, IReadOnlyList<string> args);
}

public sealed class BlenderProcess : IBlenderProcess
{
    public BlenderRun Run(string exe, IReadOnlyList<string> args, IReadOnlyDictionary<string, string>? env = null, TimeSpan? timeout = null)
    {
        var info = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        foreach (var (key, value) in env ?? new Dictionary<string, string>()) info.Environment[key] = value;
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {exe}.");
        var output = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(timeout ?? TimeSpan.FromMinutes(2)))
        {
            process.Kill(entireProcessTree: true);
            lock (output) return new BlenderRun(-1, output + "\n(timed out)");
        }
        process.WaitForExit(); // flush the async readers
        lock (output) return new BlenderRun(process.ExitCode, output.ToString());
    }

    public void Start(string exe, IReadOnlyList<string> args)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        Process.Start(info)?.Dispose();
    }
}
