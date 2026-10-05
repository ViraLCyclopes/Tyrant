using System.Runtime.CompilerServices;
using Tyrant.Core.Blender;
using Tyrant.Core.Dumping;
using Tyrant.Core.Install;
using Tyrant.Core.Tests;

namespace Tyrant.Cli.Tests;

/// <summary>
/// CLI tests never look at the real game or the real Blender: "is Prehistoric Kingdom running?" is always no, nothing is
/// launched, no Blender is found and no Blender listens.
/// </summary>
public static class CliTestSetup
{
    private sealed class NeverRunning : IGameLauncher
    {
        public bool IsRunning(GameInstall install) => false;
        public void Launch(GameInstall install) => throw new InvalidOperationException("CLI tests must not start the game.");
    }

    private sealed class NeverListening : IBlenderLink
    {
        public bool TryOpen(string projectFile) => false;
    }

    private sealed class NoSteam : ISteamRootProvider
    {
        public string? GetSteamRoot() => null;
    }

#pragma warning disable CA2255 // a module initializer in a test assembly is the point here
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void UseFakeLauncher()
    {
        CliServices.Launcher = new NeverRunning();
        CliServices.Blender = new BlenderEnvironment
        {
            Process = new FakeBlenderProcess(), Link = new NeverListening(), Steam = new NoSteam(),
            ProgramFiles = Path.Combine(Path.GetTempPath(), "tyrant-tests", "no-program-files"),
            AppData = Path.Combine(Path.GetTempPath(), "tyrant-tests", "no-appdata"),
        };
    }
}
