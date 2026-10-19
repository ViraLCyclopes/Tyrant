using Tyrant.Cli;
using Tyrant.Core.Tests;

namespace Tyrant.Cli.Tests;

public class ModSharingCliTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        var (o, e) = (new StringWriter(), new StringWriter());
        Console.SetOut(o);
        Console.SetError(e);
        try { return (CliApp.Run(args), o.ToString(), e.ToString()); }
        finally { Console.SetOut(oldOut); Console.SetError(oldErr); }
    }

    private static string Workspace(FakeGame game)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws");
        Assert.Equal(ExitCodes.Ok, Run("workspace", "init", dir, "--game", game.Root).Code);
        return dir;
    }

    [Fact]
    public void Mod_export_then_import_into_another_workspace()
    {
        using var game = new FakeGame();
        var ws1 = Workspace(game);
        Run("mod", "new", "red-spot", "--name", "Red spot", "-w", ws1);
        var zip = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "red-spot.zip");
        var ws2 = Workspace(game);

        var exported = Run("mod", "export", "red-spot", "--out", zip, "-w", ws1);
        var imported = Run("mod", "import", zip, "-w", ws2);
        var again = Run("mod", "import", zip, "-w", ws2);
        var replaced = Run("mod", "import", zip, "--replace", "-w", ws2);

        Assert.Equal(ExitCodes.Ok, exported.Code);
        Assert.True(File.Exists(zip));
        Assert.Equal(ExitCodes.Ok, imported.Code);
        Assert.Contains("red-spot", imported.Out);
        Assert.Equal(ExitCodes.Error, again.Code);
        Assert.Contains("MOD_EXISTS", again.Err);
        Assert.Equal(ExitCodes.Ok, replaced.Code);
    }

    [Fact]
    public void Mod_export_cooks_the_textures_into_the_zip()
    {
        using var game = new FakeGame();
        var ws = Workspace(game);
        Run("mod", "new", "red-spot", "--name", "Red spot", "-w", ws);
        var dir = Path.Combine(ws, "mods", "red-spot");
        Directory.CreateDirectory(Path.Combine(dir, "skins", "blue"));
        using (var stream = File.Create(Path.Combine(dir, "skins", "blue", "male_D.png")))
            new StbImageWriteSharp.ImageWriter().WritePng(new byte[4 * 4 * 4], 4, 4, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        File.WriteAllText(Path.Combine(dir, "mod.json"),
            "{\"format\":1,\"id\":\"red-spot\",\"name\":\"Red spot\",\"version\":\"1.0.0\",\"replace\":[],\"skins\":[{\"id\":\"blue\",\"species\":\"Carcharodontosaurus\",\"name\":\"Blue\",\"base\":\"0\",\"male\":{\"diffuse\":\"skins/blue/male_D.png\"}}]}");
        var zip = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "red-spot.zip");

        var exported = Run("mod", "export", "red-spot", "--out", zip, "-w", ws);

        Assert.Equal(ExitCodes.Ok, exported.Code);
        using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
        using var entry = archive.GetEntry("UserData/Tyrant/Mods/red-spot/textures.tpack")!.Open();
        using var copy = new MemoryStream();
        entry.CopyTo(copy);
        copy.Position = 0;
        Assert.Equal("skins/blue/male_D.png", Assert.Single(Tyrant.Framework.Core.TexturePack.Read(copy).Entries).Source);
        Assert.Contains("Cooking textures", exported.Out);
    }

    [Fact]
    public void Game_package_framework_writes_the_zip()
    {
        using var game = new FakeGame();
        var output = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "fw.zip");
        var dumper = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "dumper");
        Directory.CreateDirectory(dumper);
        File.WriteAllText(Path.Combine(dumper, "Tyrant.Framework.dll"), "framework");
        File.WriteAllText(Path.Combine(dumper, "Tyrant.Framework.Core.dll"), "framework library");
        var shipped = CliServices.GameModsDir;
        CliServices.GameModsDir = dumper; // the test folder has no dumper/ next to it
        try
        {
            var (code, _, err) = Run("game", "package-framework", "--out", output, "-w", Workspace(game));

            Assert.True(code == ExitCodes.Ok, err);
            Assert.True(File.Exists(output));
        }
        finally
        {
            CliServices.GameModsDir = shipped;
        }
    }

    [Fact]
    public void Game_package_framework_needs_no_workspace_with_out()
    {
        var output = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "fw.zip");
        var dumper = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "dumper");
        Directory.CreateDirectory(dumper);
        File.WriteAllText(Path.Combine(dumper, "Tyrant.Framework.dll"), "framework");
        var shipped = CliServices.GameModsDir;
        CliServices.GameModsDir = dumper;
        try
        {
            var (code, _, err) = Run("game", "package-framework", "--out", output);

            Assert.True(code == ExitCodes.Ok, err);
            Assert.True(File.Exists(output));
        }
        finally
        {
            CliServices.GameModsDir = shipped;
        }
    }

    [Fact]
    public void Mod_cook_writes_the_pack_into_the_workspace_folder_without_installing()
    {
        using var game = new FakeGame();
        var ws = Workspace(game);
        Run("mod", "new", "red-spot", "--name", "Red spot", "-w", ws);
        var dir = Path.Combine(ws, "mods", "red-spot");
        Directory.CreateDirectory(Path.Combine(dir, "skins", "blue"));
        using (var stream = File.Create(Path.Combine(dir, "skins", "blue", "male_D.png")))
            new StbImageWriteSharp.ImageWriter().WritePng(new byte[4 * 4 * 4], 4, 4, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        File.WriteAllText(Path.Combine(dir, "mod.json"),
            "{\"format\":1,\"id\":\"red-spot\",\"name\":\"Red spot\",\"version\":\"1.0.0\",\"replace\":[],\"skins\":[{\"id\":\"blue\",\"species\":\"Carcharodontosaurus\",\"name\":\"Blue\",\"base\":\"0\",\"male\":{\"diffuse\":\"skins/blue/male_D.png\"}}]}");

        var (code, output, err) = Run("mod", "cook", "red-spot", "-w", ws);

        Assert.True(code == ExitCodes.Ok, err + output);
        Assert.Contains("Cooking textures 1/1", output);
        var pack = Path.Combine(dir, "textures.tpack");
        Assert.Contains(pack, output);
        using (var file = File.OpenRead(pack))
            Assert.Equal("skins/blue/male_D.png", Assert.Single(Tyrant.Framework.Core.TexturePack.Read(file).Entries).Source);
        Assert.False(Directory.Exists(Path.Combine(game.Root, "UserData", "Tyrant", "Mods", "red-spot")));
    }

    [Fact]
    public void Mod_install_cooks_the_textures_into_the_games_mod_folder()
    {
        using var game = new FakeGame();
        var ws = Workspace(game);
        Run("mod", "new", "red-spot", "--name", "Red spot", "-w", ws);
        var dir = Path.Combine(ws, "mods", "red-spot");
        Directory.CreateDirectory(Path.Combine(dir, "skins", "blue"));
        using (var stream = File.Create(Path.Combine(dir, "skins", "blue", "male_D.png")))
            new StbImageWriteSharp.ImageWriter().WritePng(new byte[4 * 4 * 4], 4, 4, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        File.WriteAllText(Path.Combine(dir, "mod.json"),
            "{\"format\":1,\"id\":\"red-spot\",\"name\":\"Red spot\",\"version\":\"1.0.0\",\"replace\":[],\"skins\":[{\"id\":\"blue\",\"species\":\"Carcharodontosaurus\",\"name\":\"Blue\",\"base\":\"0\",\"male\":{\"diffuse\":\"skins/blue/male_D.png\"}}]}");

        // The framework is installed up front (with a fake loader archive and its hash), so 'mod install' needs no download.
        var componentDir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "dumper");
        Directory.CreateDirectory(componentDir);
        foreach (var name in new[] { "Tyrant.Dumper.dll", "Tyrant.Dumper.Serialization.dll", "Tyrant.Framework.dll", "Tyrant.Framework.Core.dll" })
            File.WriteAllText(Path.Combine(componentDir, name), "fake " + name);
        var loader = Path.Combine(Path.GetDirectoryName(componentDir)!, "MelonLoader.x64.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(loader, System.IO.Compression.ZipArchiveMode.Create))
            foreach (var entry in new[] { "version.dll", "MelonLoader/net35/MelonLoader.dll", "MelonLoader/net472/MelonLoader.dll" })
            {
                using var w = new StreamWriter(zip.CreateEntry(entry).Open());
                w.Write("fake " + entry);
            }
        string sha;
        using (var stream = File.OpenRead(loader)) sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
        new Tyrant.Core.Dumping.ModLoaderInstaller(sha, _ => false).Install(new Tyrant.Core.Install.GameInstall(game.Root, null), loader, componentDir);

        var shipped = CliServices.GameModsDir;
        CliServices.GameModsDir = componentDir;
        try
        {
            var installed = Run("mod", "install", "red-spot", "-w", ws);

            Assert.True(installed.Code == ExitCodes.Ok, installed.Err + installed.Out);
            Assert.Contains("Cooking textures 1/1", installed.Out);
            var pack = Path.Combine(game.Root, "UserData", "Tyrant", "Mods", "red-spot", "textures.tpack");
            using var file = File.OpenRead(pack);
            Assert.Equal("skins/blue/male_D.png", Assert.Single(Tyrant.Framework.Core.TexturePack.Read(file).Entries).Source);
            Assert.True(File.Exists(Path.Combine(dir, "textures.tpack"))); // the workspace gets the same pack
        }
        finally
        {
            CliServices.GameModsDir = shipped;
        }
    }

    private static string ModWithOneTexture(string ws)
    {
        Run("mod", "new", "red-spot", "--name", "Red spot", "-w", ws);
        var dir = Path.Combine(ws, "mods", "red-spot");
        Directory.CreateDirectory(Path.Combine(dir, "skins", "blue"));
        using (var stream = File.Create(Path.Combine(dir, "skins", "blue", "male_D.png")))
            new StbImageWriteSharp.ImageWriter().WritePng(new byte[4 * 4 * 4], 4, 4, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        File.WriteAllText(Path.Combine(dir, "mod.json"),
            "{\"format\":1,\"id\":\"red-spot\",\"name\":\"Red spot\",\"version\":\"1.0.0\",\"replace\":[],\"skins\":[{\"id\":\"blue\",\"species\":\"Carcharodontosaurus\",\"name\":\"Blue\",\"base\":\"0\",\"male\":{\"diffuse\":\"skins/blue/male_D.png\"}}]}");
        return dir;
    }

    [Fact]
    public void Mod_export_leaves_out_the_pngs_unless_with_pngs_and_import_extracts_them()
    {
        using var game = new FakeGame();
        var ws = Workspace(game);
        ModWithOneTexture(ws);
        var temp = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        var (slim, full) = (Path.Combine(temp, "slim.zip"), Path.Combine(temp, "full.zip"));

        Assert.Equal(ExitCodes.Ok, Run("mod", "export", "red-spot", "--out", slim, "-w", ws).Code);
        Assert.Equal(ExitCodes.Ok, Run("mod", "export", "red-spot", "--out", full, "--with-pngs", "-w", ws).Code);
        var imported = Run("mod", "import", slim, "-w", Workspace(game));

        using (var archive = System.IO.Compression.ZipFile.OpenRead(slim))
            Assert.Null(archive.GetEntry("UserData/Tyrant/Mods/red-spot/skins/blue/male_D.png"));
        using (var archive = System.IO.Compression.ZipFile.OpenRead(full))
            Assert.NotNull(archive.GetEntry("UserData/Tyrant/Mods/red-spot/skins/blue/male_D.png"));
        Assert.Equal(ExitCodes.Ok, imported.Code);
        Assert.Contains("1 textures were extracted from its cooked pack; they are slightly lower quality than the author's originals.", imported.Out);
    }

    [Fact]
    public void Tpack_extract_writes_a_packs_pngs_without_a_workspace()
    {
        using var game = new FakeGame();
        var ws = Workspace(game);
        var dir = ModWithOneTexture(ws);
        Run("mod", "cook", "red-spot", "-w", ws);
        var outDir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "out");

        var (code, output, err) = Run("tpack", "extract", Path.Combine(dir, "textures.tpack"), "--out", outDir);

        Assert.True(code == ExitCodes.Ok, err + output);
        Assert.Contains($"Extracted 1 textures into {outDir}.", output);
        Assert.True(File.Exists(Path.Combine(outDir, "skins", "blue", "male_D.png")));
    }

    [Fact]
    public void Mod_copy_from_game_brings_an_installed_mod_into_the_workspace()
    {
        using var game = new FakeGame();
        var ws = Workspace(game);
        ModWithOneTexture(ws);
        var installed = Path.Combine(game.Root, "UserData", "Tyrant", "Mods", "red-spot");
        Directory.CreateDirectory(Path.Combine(installed, "skins", "blue"));
        foreach (var file in new[] { "mod.json", "skins/blue/male_D.png" })
            File.Copy(Path.Combine(ws, "mods", "red-spot", file), Path.Combine(installed, file));
        var other = Workspace(game);

        var (code, output, err) = Run("mod", "copy-from-game", "red-spot", "-w", other);

        Assert.True(code == ExitCodes.Ok, err + output);
        Assert.True(File.Exists(Path.Combine(other, "mods", "red-spot", "skins", "blue", "male_D.png")));
    }
    /// <summary>A workspace with red-spot, and a game with a stand-in framework that matches the one Tyrant ships (no download).</summary>
    private static (string Ws, string Installed, string Shipped) InstalledModSetup(FakeGame game)
    {
        var ws = Workspace(game);
        ModWithOneTexture(ws);
        var shipped = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "dumper");
        Directory.CreateDirectory(shipped);
        File.WriteAllText(Path.Combine(shipped, "Tyrant.Framework.dll"), "framework");
        Directory.CreateDirectory(Path.Combine(game.Root, "Mods"));
        File.WriteAllText(Path.Combine(game.Root, "Mods", "Tyrant.Framework.dll"), "framework");
        Directory.CreateDirectory(Path.Combine(game.Root, "UserData", "Tyrant"));
        File.WriteAllText(Path.Combine(game.Root, "UserData", "Tyrant", "install.json"), """{"installedLoader":true,"files":[],"createdDirs":[]}""");
        return (ws, Path.Combine(game.Root, "UserData", "Tyrant", "Mods", "red-spot"), shipped);
    }

    /// <summary>What the in-game editor saves: a species' materials.json in the installed copy.</summary>
    private static void SaveLookInGame(string installed, string strength)
    {
        var folder = Path.Combine(installed, "materials", "species", "Carcharodontosaurus");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "materials.json"), """{"format":1,"species":"Carcharodontosaurus","materials":{"Carch":{"iridescence":{"strength":""" + strength + "}}}}");
    }

    [Fact]
    public void Install_refuses_while_game_looks_are_pending()
    {
        using var game = new FakeGame();
        var (ws, installed, shipped) = InstalledModSetup(game);
        var componentDir = CliServices.GameModsDir;
        CliServices.GameModsDir = shipped;
        try
        {
            var first = Run("mod", "install", "red-spot", "-w", ws);
            Assert.True(first.Code == ExitCodes.Ok, first.Err + first.Out);
            SaveLookInGame(installed, "0.9");

            var install = Run("mod", "install", "red-spot", "-w", ws);
            var remove = Run("mod", "remove", "red-spot", "-w", ws);
            var looks = Run("mod", "looks", "red-spot", "-w", ws);

            Assert.Equal(ExitCodes.Error, install.Code);
            Assert.Contains("GAME_LOOKS_PENDING", install.Err);
            Assert.Contains("materials/species/Carcharodontosaurus", install.Err);
            Assert.Equal(ExitCodes.Error, remove.Code);
            Assert.Contains("GAME_LOOKS_PENDING", remove.Err);
            Assert.True(Directory.Exists(installed));
            Assert.Equal(ExitCodes.Ok, looks.Code);
            Assert.Contains("materials/species/Carcharodontosaurus", looks.Out);
            Assert.Contains("changed in the game", looks.Out);

            var brought = Run("mod", "install", "red-spot", "--bring-in-looks", "-w", ws);
            Assert.True(brought.Code == ExitCodes.Ok, brought.Err + brought.Out);
            Assert.Contains("0.9", File.ReadAllText(Path.Combine(ws, "mods", "red-spot", "materials", "species", "Carcharodontosaurus", "materials.json")));

            SaveLookInGame(installed, "0.3");
            var discarded = Run("mod", "remove", "red-spot", "--discard-game-looks", "-w", ws);
            Assert.True(discarded.Code == ExitCodes.Ok, discarded.Err + discarded.Out);
            Assert.False(Directory.Exists(installed));
            Assert.Contains("0.9", File.ReadAllText(Path.Combine(ws, "mods", "red-spot", "materials", "species", "Carcharodontosaurus", "materials.json")));
        }
        finally
        {
            CliServices.GameModsDir = componentDir;
        }
    }

    [Fact]
    public void Mod_looks_brings_in_the_chosen_folders()
    {
        using var game = new FakeGame();
        var (ws, installed, shipped) = InstalledModSetup(game);
        var componentDir = CliServices.GameModsDir;
        CliServices.GameModsDir = shipped;
        try
        {
            Assert.Equal(ExitCodes.Ok, Run("mod", "install", "red-spot", "-w", ws).Code);
            SaveLookInGame(installed, "0.9");
            var vanilla = Path.Combine(installed, "materials", "vanilla-skins", "Carcharodontosaurus-Alt 1");
            Directory.CreateDirectory(vanilla);
            File.WriteAllText(Path.Combine(vanilla, "materials.json"), """{"format":1,"species":"Carcharodontosaurus","skin":"Alt 1","materials":{"Carch":{"iridescence":{"strength":0.4}}}}""");

            var (code, output, err) = Run("mod", "looks", "red-spot", "--bring-in", "--folder", "materials/species/Carcharodontosaurus", "-w", ws);
            var left = Run("mod", "looks", "red-spot", "-w", ws);

            Assert.True(code == ExitCodes.Ok, err + output);
            Assert.True(File.Exists(Path.Combine(ws, "mods", "red-spot", "materials", "species", "Carcharodontosaurus", "materials.json")));
            Assert.False(Directory.Exists(Path.Combine(ws, "mods", "red-spot", "materials", "vanilla-skins")));
            Assert.Contains("Carcharodontosaurus-Alt 1", left.Out);
            Assert.DoesNotContain("materials/species/", left.Out);
        }
        finally
        {
            CliServices.GameModsDir = componentDir;
        }
    }
}
