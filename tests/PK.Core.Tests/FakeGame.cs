using PK.Core.Install;

namespace PK.Core.Tests;

/// <summary>
/// Builds a synthetic install: &lt;temp&gt;/lib/steamapps/common/&lt;folder&gt;/ with an empty exe,
/// a boot.config and PK.Core.dll copied in as Assembly-CSharp.dll (no real game content).
/// </summary>
public sealed class FakeGame : IDisposable
{
    public string LibraryRoot { get; }
    public string SteamApps { get; }
    public string Root { get; }

    public FakeGame(string? buildGuid = "abc123def456", string folderName = "Prehistoric Kingdom")
    {
        LibraryRoot = Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N"), "lib");
        SteamApps = Path.Combine(LibraryRoot, "steamapps");
        Root = Path.Combine(SteamApps, "common", folderName);
        var dataDir = Path.Combine(Root, GameInstall.DataDirName);
        var managed = Path.Combine(dataDir, "Managed");
        Directory.CreateDirectory(managed);
        File.WriteAllText(Path.Combine(Root, GameInstall.ExeName), "");
        File.Copy(typeof(KeyValuesParser).Assembly.Location, Path.Combine(managed, "Assembly-CSharp.dll"));
        if (buildGuid is not null)
            File.WriteAllText(Path.Combine(dataDir, "boot.config"), $"gfx-enable-gfx-jobs=1\nbuild-guid={buildGuid}\nsingle-instance=\n");
    }

    public void WriteAppManifest(string appId, string? installDir = null) =>
        File.WriteAllText(
            Path.Combine(SteamApps, $"appmanifest_{appId}.acf"),
            $"\"AppState\"\n{{\n\t\"appid\"\t\t\"{appId}\"\n\t\"installdir\"\t\t\"{installDir ?? Path.GetFileName(Root)}\"\n}}\n");

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(LibraryRoot)!, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
