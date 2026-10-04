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
        Assert.Contains("https://github.com/ViraLCyclopes/Tyrant/releases", readme);
    }

    [Fact]
    public void A_manifest_path_outside_the_mod_is_never_shared()
    {
        using var game = new FakeGame();
        var mod = ModWithFiles(NewWorkspace(game));
        mod.Manifest.Replace.Add(new TextureReplacement { Texture = "b", File = "../../evil.png" });

        Assert.DoesNotContain(ModSharing.SharedFiles(mod), f => f.Contains("evil"));
    }

    private static string ZipWith(params (string Name, string Content)[] entries)
    {
        var path = TempZip();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var w = new StreamWriter(archive.CreateEntry(name).Open());
            w.Write(content);
        }
        return path;
    }

    private const string Manifest = """{ "format": 1, "id": "shared-mod", "name": "Shared", "version": "1.0.0" }""";

    [Fact]
    public void An_exported_zip_comes_back_as_a_workspace_mod()
    {
        using var game = new FakeGame();
        var ws = NewWorkspace(game);
        var zip = ModSharing.Export(ModWithFiles(ws), TempZip(), "0.3.0");
        var other = NewWorkspace(game);

        var mod = ModSharing.Import(other, zip, replace: false);

        Assert.Equal("red-spot", mod.Id);
        Assert.True(File.Exists(Path.Combine(mod.Dir, "textures", "a.png")));
        Assert.False(File.Exists(Path.Combine(mod.Dir, "README.txt")));
    }

    [Theory]
    [InlineData("shared-mod/")]                           // a zip of the mod folder
    [InlineData("")]                                      // mod.json at the root
    [InlineData("UserData\\Tyrant\\Mods\\shared-mod\\")] // backslashes, as some Windows zippers write
    public void Other_layouts_import_too(string prefix)
    {
        using var game = new FakeGame();
        var zip = ZipWith((prefix + "mod.json", Manifest), (prefix + "textures/x.png", "png"));

        var mod = ModSharing.Import(NewWorkspace(game), zip, replace: false);

        Assert.Equal("shared-mod", mod.Id);
        Assert.True(File.Exists(Path.Combine(mod.Dir, "textures", "x.png")));
    }

    [Fact]
    public void A_path_that_escapes_the_mod_refuses_the_whole_zip()
    {
        using var game = new FakeGame();
        var ws = NewWorkspace(game);
        var zip = ZipWith(("shared-mod/mod.json", Manifest), ("shared-mod/../../evil.txt", "x"));

        var ex = Assert.Throws<Tyrant.Core.Errors.TyrantException>(() => ModSharing.Import(ws, zip, replace: false));

        Assert.Contains("unsafe", ex.Message);
        Assert.False(Directory.Exists(Path.Combine(ModProject.RootOf(ws), "shared-mod")));
    }

    [Fact]
    public void A_zip_without_a_mod_or_with_two_is_refused()
    {
        using var game = new FakeGame();
        var ws = NewWorkspace(game);
        Assert.Throws<Tyrant.Core.Errors.TyrantException>(() => ModSharing.Import(ws, ZipWith(("readme.txt", "hi")), false));
        Assert.Throws<Tyrant.Core.Errors.TyrantException>(() => ModSharing.Import(ws,
            ZipWith(("a-mod/mod.json", Manifest.Replace("shared-mod", "a-mod")), ("b-mod/mod.json", Manifest.Replace("shared-mod", "b-mod"))), false));
    }

    [Fact]
    public void A_zip_of_a_renamed_mod_folder_imports_under_its_mod_json_id()
    {
        using var game = new FakeGame();
        // e.g. GitHub's Download ZIP: <repo>-main/mod.json
        var mod = ModSharing.Import(NewWorkspace(game), ZipWith(("Tyrant-mods-main/mod.json", Manifest), ("Tyrant-mods-main/a.png", "png")), false);

        Assert.Equal("shared-mod", mod.Id);
        Assert.True(File.Exists(Path.Combine(mod.Dir, "a.png")));
    }

    [Fact]
    public void A_mod_json_with_an_invalid_id_is_refused()
    {
        using var game = new FakeGame();
        Assert.Throws<Tyrant.Core.Errors.TyrantException>(() =>
            ModSharing.Import(NewWorkspace(game), ZipWith(("x/mod.json", Manifest.Replace("shared-mod", "Not Valid"))), false));
    }

    // (1) damaged zips
    [Fact]
    public void A_file_that_is_not_a_zip_is_a_clear_error()
    {
        using var game = new FakeGame();
        var path = TempZip();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "this is a renamed .rar, not a zip");

        var ex = Assert.Throws<Tyrant.Core.Errors.TyrantException>(() => ModSharing.Import(NewWorkspace(game), path, false));

        Assert.Equal(Tyrant.Core.Errors.TyrantErrorCode.ModInvalid, ex.Code);
        Assert.Contains("damaged or not a zip", ex.Message);
    }

    // (2) no local paths, (3) not stale after import, (5) the framework it needs
    private static ModProject ModWithBuiltModel(Workspace ws)
    {
        var mod = ModProject.Create(ws, "big-carch", "Big carch", null);
        var models = Path.Combine(mod.Dir, "models");
        Directory.CreateDirectory(models);
        File.WriteAllText(Path.Combine(models, "carch-1111.glb"), "glb");
        File.WriteAllText(Path.Combine(models, "carch-1111.lod0.tmesh"), "lod0");
        var report = new Tyrant.Core.ModelReplacements.ModelReport("models/carch-1111.glb",
            Tyrant.Core.ModelReplacements.ModelBuilder.Stamp(Path.Combine(models, "carch-1111.glb")), [], [],
            [new Tyrant.Core.ModelReplacements.ModelLodStats("models/carch-1111.lod0.tmesh", 3, false, 3)],
            @"C:\Users\someone\Desktop\carch.glb", "1|2");
        File.WriteAllText(Path.Combine(models, "carch-1111.model.json"), System.Text.Json.JsonSerializer.Serialize(report,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
        mod.Manifest.Models.Add(new ModelReplacement { Target = "Carcharodontosaurus", File = "models/carch-1111.glb" });
        mod.Save();
        return mod;
    }

    private static string Entry(string zip, string name)
    {
        using var archive = ZipFile.OpenRead(zip);
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void A_shared_model_carries_no_local_paths()
    {
        using var game = new FakeGame();
        var zip = ModSharing.Export(ModWithBuiltModel(NewWorkspace(game)), TempZip(), "0.3.0");

        var report = Entry(zip, "UserData/Tyrant/Mods/big-carch/models/carch-1111.model.json");

        Assert.DoesNotContain("Users", report);
        Assert.DoesNotContain("Desktop", report);
    }

    [Fact]
    public void An_imported_model_is_not_reported_as_changed()
    {
        using var game = new FakeGame();
        var zip = ModSharing.Export(ModWithBuiltModel(NewWorkspace(game)), TempZip(), "0.3.0");

        var mod = ModSharing.Import(NewWorkspace(game), zip, false);

        Assert.False(Tyrant.Core.ModelReplacements.ModelBuilder.IsStale(mod.Dir, "models/carch-1111.glb"));
    }

    [Fact]
    public void The_shared_mod_json_names_the_framework_it_needs()
    {
        using var game = new FakeGame();
        var zip = ModSharing.Export(ModWithFiles(NewWorkspace(game)), TempZip(), "0.3.0");

        Assert.Equal("0.3.0", ModManifest.Parse(Entry(zip, "UserData/Tyrant/Mods/red-spot/mod.json")).RequiresTyrant);
    }

    [Fact]
    public void An_existing_mod_is_kept_unless_replace_is_given()
    {
        using var game = new FakeGame();
        var ws = NewWorkspace(game);
        var zip = ZipWith(("shared-mod/mod.json", Manifest), ("shared-mod/new.png", "new"));
        ModSharing.Import(ws, ZipWith(("shared-mod/mod.json", Manifest), ("shared-mod/old.png", "old")), false);

        var ex = Assert.Throws<Tyrant.Core.Errors.TyrantException>(() => ModSharing.Import(ws, zip, false));
        Assert.Equal(Tyrant.Core.Errors.TyrantErrorCode.ModExists, ex.Code);

        var mod = ModSharing.Import(ws, zip, replace: true);
        Assert.True(File.Exists(Path.Combine(mod.Dir, "new.png")));
        Assert.False(File.Exists(Path.Combine(mod.Dir, "old.png")));
    }

    [Fact]
    public void A_damaged_zip_leaves_the_existing_mod_as_it_was()
    {
        using var game = new FakeGame();
        var ws = NewWorkspace(game);
        ModSharing.Import(ws, ZipWith(("shared-mod/mod.json", Manifest), ("shared-mod/old.png", "old")), false);
        var damaged = ZipWith(("shared-mod/mod.json", Manifest), ("shared-mod/big.png", string.Concat(Enumerable.Range(0, 3000).Select(i => (char)('a' + i * 7 % 26)))));
        var bytes = File.ReadAllBytes(damaged);
        // The entry's data follows its name in the local header (the first time the name appears): corrupt it.
        var name = System.Text.Encoding.ASCII.GetBytes("shared-mod/big.png");
        var at = Enumerable.Range(0, bytes.Length - name.Length).First(i => bytes.AsSpan(i, name.Length).SequenceEqual(name)) + name.Length;
        for (var i = at; i < at + 64; i++) bytes[i] ^= 0x5A;
        File.WriteAllBytes(damaged, bytes);

        var ex = Assert.Throws<Tyrant.Core.Errors.TyrantException>(() => ModSharing.Import(ws, damaged, replace: true));
        Assert.Equal(Tyrant.Core.Errors.TyrantErrorCode.ModInvalid, ex.Code);

        Assert.True(File.Exists(Path.Combine(ModProject.RootOf(ws), "shared-mod", "old.png")));
    }
}
