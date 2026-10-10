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
        }
        finally
        {
            CliServices.GameModsDir = shipped;
        }
    }
}
