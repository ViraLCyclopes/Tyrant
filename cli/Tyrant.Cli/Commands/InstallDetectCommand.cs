using Tyrant.Core.Install;
using Spectre.Console.Cli;

namespace Tyrant.Cli.Commands;

public sealed class InstallDetectCommand : Command<GameSettings>
{
    public override int Execute(CommandContext context, GameSettings settings)
    {
        var install = CliServices.ResolveInstall(settings.GamePath);
        var fp = GameFingerprint.Compute(install);
        Console.WriteLine($"Game folder : {install.RootDir}");
        Console.WriteLine($"Steam app id: {install.SteamAppId ?? "(unknown)"}");
        Console.WriteLine($"Build GUID  : {fp.BuildGuid}");
        Console.WriteLine($"Code SHA-256: {fp.AssemblySha256}");
        return ExitCodes.Ok;
    }
}
