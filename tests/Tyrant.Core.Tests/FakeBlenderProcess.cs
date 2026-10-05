using Tyrant.Core.Blender;

namespace Tyrant.Core.Tests;

/// <summary>Answers "blender --version" per exe and records every run/start; never starts a process.</summary>
public sealed class FakeBlenderProcess : IBlenderProcess
{
    public Dictionary<string, string> Versions { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Exe, IReadOnlyList<string> Args, IReadOnlyDictionary<string, string>? Env)> Runs { get; } = [];
    public List<(string Exe, IReadOnlyList<string> Args)> Starts { get; } = [];
    public Func<string, IReadOnlyList<string>, BlenderRun>? OnRun { get; set; }

    public BlenderRun Run(string exe, IReadOnlyList<string> args, IReadOnlyDictionary<string, string>? env = null, TimeSpan? timeout = null)
    {
        Runs.Add((exe, args, env));
        if (args is ["--version"]) return Versions.TryGetValue(exe, out var v) ? new BlenderRun(0, v) : new BlenderRun(1, "");
        // Like Blender: the add-on's enable script confirms it turned the add-on on.
        return OnRun?.Invoke(exe, args) ?? new BlenderRun(0, args.Contains("--python-expr") ? "TYRANT-ADDON-ENABLED" : "");
    }

    public void Start(string exe, IReadOnlyList<string> args) => Starts.Add((exe, args));
}
