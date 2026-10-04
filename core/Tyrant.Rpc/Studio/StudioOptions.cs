using Tyrant.Core.Assets;
using Tyrant.Core.Dumping;
using Tyrant.Core.Install;
using Tyrant.Core.Workspaces;

namespace Tyrant.Rpc.Studio;

/// <summary>The services Studio's methods use; tests replace Steam, downloads and the game.</summary>
public sealed class StudioOptions
{
    public required GameInstallLocator Locator { get; init; }
    public required IGameLauncher Launcher { get; init; }

    /// <summary>Folder holding Tyrant.Dumper*.dll (next to tyrant.exe in production).</summary>
    public required string DumperDir { get; init; }

    /// <summary>Returns the MelonLoader archive for a fresh install (downloads it in production).</summary>
    public required Func<Workspace, CancellationToken, string> LoaderZip { get; init; }

    public required Func<ModLoaderInstaller> Installer { get; init; }

    /// <summary>Reads the game's bundles (previews, exports, species packs); tests use a fake.</summary>
    public IAssetReader AssetReader { get; init; } = new BundleAssetReader();

    /// <summary>HTTP for update checks; tests pass a fake.</summary>
    public Func<HttpClient> Http { get; init; } = () => new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
}
