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
        using var process = Process.Start(RunInfo(exe, args, env)) ?? throw new InvalidOperationException($"Could not start {exe}.");
        process.StandardInput.Close(); // nothing to read: Blender sees the end of its input at once
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

    /// <summary>
    /// A Blender run Tyrant waits for: its output read, and its own (closed) input. Inheriting the core's input in the app (the
    /// app's request pipe) made 'blender --command extension install-file' wait on it forever.
    /// </summary>
    internal static ProcessStartInfo RunInfo(string exe, IReadOnlyList<string> args, IReadOnlyDictionary<string, string>? env)
    {
        var info = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        foreach (var (key, value) in env ?? new Dictionary<string, string>()) info.Environment[key] = value;
        return info;
    }

    public void Start(string exe, IReadOnlyList<string> args) => Process.Start(StartInfo(exe, args))?.Dispose();

    /// <summary>
    /// Blender started for the user through the shell: it gets its own console and none of Tyrant's handles (the sidecar's
    /// stdout is the app's protocol pipe; an inherited copy would mix Blender's output in and keep the pipe open).
    /// </summary>
    internal static ProcessStartInfo StartInfo(string exe, IReadOnlyList<string> args)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) ?? "" };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return info;
    }
}
