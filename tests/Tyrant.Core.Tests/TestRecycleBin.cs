using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Tyrant.Core.Mods;

namespace Tyrant.Core.Tests;

/// <summary>Stands in for the Recycle Bin in every test assembly: moves each item under %TEMP%\tyrant-tests\recycled and remembers it.</summary>
public sealed class TestRecycleBin : IRecycleBin
{
    public static readonly TestRecycleBin Instance = new();
    private readonly ConcurrentBag<string> _recycled = [];
    private readonly ConcurrentBag<string> _refused = [];

    /// <summary>True when this path (as given) went to the bin.</summary>
    public bool Holds(string path) => _recycled.Contains(Path.GetFullPath(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Anything under this folder fails to recycle, like a file open in another program.</summary>
    public void RefuseUnder(string dir) => _refused.Add(Path.GetFullPath(dir));

    public void Recycle(string path)
    {
        var full = Path.GetFullPath(path);
        if (_refused.Any(dir => full.StartsWith(dir, StringComparison.OrdinalIgnoreCase)))
            throw new IOException($"'{full}' is in use by another program.");
        var to = Path.Combine(TestTemp.Root, "recycled", Guid.NewGuid().ToString("N"), Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (Directory.Exists(full)) Directory.Move(full, to);
        else if (File.Exists(full)) File.Move(full, to);
        else throw new IOException($"'{full}' does not exist.");
        _recycled.Add(full);
    }

    public static void Install() => RecycleBin.Current = Instance;

#pragma warning disable CA2255 // a module initializer in a test assembly is the point here
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void UseInTheseTests() => Install();
}
