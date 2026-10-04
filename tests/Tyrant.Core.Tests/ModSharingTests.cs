using System.IO.Compression;
using Tyrant.Core.Install;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Tests;

public class ModSharingTests
{
    private static Workspace NewWorkspace(FakeGame game) =>
        Workspace.Create(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws"), new GameInstall(game.Root, null));

    private static ModProject ModWithFiles(Workspace ws)
    {
        var mod = ModProject.Create(ws, "red-spot", "Red spot", "Shadow");
        void Write(string relative) { var p = Path.Combine(mod.Dir, relative); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, relative); }
        Write("textures/a.png");
        Write("textures/unused.png");                  // in the folder, not in mod.json
        Write("models/carch-1111.glb");
        Write("models/carch-1111.lod0.tmesh");
        Write("models/carch-1111.lod1.tmesh");
        Write("models/carch-1111.model.json");
        Write("models/carch-0000.glb");                // an older build mod.json no longer names
        Write("models/carch-0000.lod0.tmesh");
        mod.Manifest.Replace.Add(new TextureReplacement { Texture = "a", File = "textures/a.png" });
        mod.Manifest.Models.Add(new ModelReplacement { Target = "Carcharodontosaurus", File = "models/carch-1111.glb" });
        mod.Save();
        return mod;
    }

    private static string TempZip() => Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "out.zip");

    [Fact]
    public void Only_the_files_mod_json_uses_are_shared()
    {
        using var game = new FakeGame();
        var mod = ModWithFiles(NewWorkspace(game));

        Assert.Equal(
            ["mod.json", "models/carch-1111.glb", "models/carch-1111.lod0.tmesh", "models/carch-1111.lod1.tmesh", "models/carch-1111.model.json", "textures/a.png"],
            ModSharing.SharedFiles(mod));
    }

    [Fact]
    public void The_zip_unzips_into_the_game_folder()
    {
        using var game = new FakeGame();
        var mod = ModWithFiles(NewWorkspace(game));

        var zip = ModSharing.Export(mod, TempZip(), "0.3.0");

        using var archive = ZipFile.OpenRead(zip);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("UserData/Tyrant/Mods/red-spot/mod.json", names);
        Assert.Contains("UserData/Tyrant/Mods/red-spot/textures/a.png", names);
        Assert.DoesNotContain("UserData/Tyrant/Mods/red-spot/textures/unused.png", names);
        Assert.DoesNotContain(names, n => n.Contains("carch-0000"));
        Assert.Contains("README.txt", names);
    }

    [Fact]
    public void The_readme_says_what_the_mod_needs_and_how_to_install_it()
    {
        using var game = new FakeGame();
        var mod = ModWithFiles(NewWorkspace(game));

        using var archive = ZipFile.OpenRead(ModSharing.Export(mod, TempZip(), "0.3.0"));
        using var reader = new StreamReader(archive.GetEntry("README.txt")!.Open());
        var readme = reader.ReadToEnd();

        Assert.Contains("Red spot", readme);
        Assert.Contains("red-spot", readme);
        Assert.Contains("Shadow", readme);
        Assert.Contains("MelonLoader 0.7.3", readme);
        Assert.Contains("Tyrant Framework 0.3.0", readme);
        Assert.Contains("UserData\\Tyrant\\Mods\\red-spot", readme);
    }

    [Fact]
    public void A_manifest_path_outside_the_mod_is_never_shared()
    {
        using var game = new FakeGame();
        var mod = ModWithFiles(NewWorkspace(game));
        mod.Manifest.Replace.Add(new TextureReplacement { Texture = "b", File = "../../evil.png" });

        Assert.DoesNotContain(ModSharing.SharedFiles(mod), f => f.Contains("evil"));
    }
}
