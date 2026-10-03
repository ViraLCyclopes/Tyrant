using System.Runtime.CompilerServices;
using Tyrant.Core.Dumping;
using Tyrant.Core.Install;

namespace Tyrant.Cli.Tests;

/// <summary>CLI tests never look at the real game: "is Prehistoric Kingdom running?" is always no, and nothing is launched.</summary>
public static class CliTestSetup
{
    private sealed class NeverRunning : IGameLauncher
    {
        public bool IsRunning(GameInstall install) => false;
        public void Launch(GameInstall install) => throw new InvalidOperationException("CLI tests must not start the game.");
    }

#pragma warning disable CA2255 // a module initializer in a test assembly is the point here
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void UseFakeLauncher() => CliServices.Launcher = new NeverRunning();
}
