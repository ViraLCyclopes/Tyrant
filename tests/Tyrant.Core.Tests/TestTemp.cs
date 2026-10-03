using System.Runtime.CompilerServices;

namespace Tyrant.Core.Tests;

/// <summary>Tests write under %TEMP%\tyrant-tests; each test assembly removes folders older than a day when it loads.</summary>
public static class TestTemp
{
    public static readonly string Root = Path.Combine(Path.GetTempPath(), "tyrant-tests");

#pragma warning disable CA2255 // a module initializer in a test assembly is the point here
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void CleanOnLoad() => Task.Run(() => CleanOld(Root, DateTime.UtcNow)); // in the background: never slows a test run

    public static int CleanOld(string root, DateTime nowUtc)
    {
        if (!Directory.Exists(root)) return 0;
        var removed = 0;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(dir) >= nowUtc.AddDays(-1)) continue;
                Directory.Delete(dir, recursive: true);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // in use by another test run, or read-only: leave it for next time
            }
        }
        return removed;
    }
}
