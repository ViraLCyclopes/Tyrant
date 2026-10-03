using System.IO.Compression;
using System.Security.Cryptography;
using PK.Core.Dumping;
using PK.Core.Errors;
using PK.Core.Install;

namespace PK.Core.Tests;

public class BepInExInstallerTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N"));

    /// <summary>A zip shaped like the BepInEx release; returns (path, sha256).</summary>
    private static (string Path, string Sha) FakeBepInExZip(params string[] extraEntries)
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "BepInEx_win_x64.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var entry in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt", "BepInEx/core/BepInEx.dll", "BepInEx/core/0Harmony.dll" }.Concat(extraEntries))
            {
                using var w = new StreamWriter(zip.CreateEntry(entry).Open());
                w.Write("fake " + entry);
            }
        }
        using var stream = File.OpenRead(path);
        return (path, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }

    private static string FakeDumperDir()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "PK.Dumper.dll"), "plugin");
        File.WriteAllText(Path.Combine(dir, "PK.Dumper.Serialization.dll"), "serializer");
        return dir;
    }

    [Fact]
    public void Install_adds_bepinex_and_plugin_and_records_them()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var (zip, sha) = FakeBepInExZip();

        var record = new BepInExInstaller(sha).Install(install, zip, FakeDumperDir());

        Assert.True(record.InstalledBepInEx);
        Assert.True(File.Exists(Path.Combine(game.Root, "winhttp.dll")));
        Assert.True(File.Exists(Path.Combine(game.Root, "BepInEx", "plugins", "PKModStudio", "PK.Dumper.dll")));
        Assert.Contains("winhttp.dll", record.Files);
        Assert.Contains("BepInEx/plugins/PKModStudio/PK.Dumper.dll", record.Files);
        Assert.Equal(InstallState.Installed, BepInExInstaller.GetState(install));
        var reread = BepInExInstaller.ReadRecord(install)!;
        Assert.Equal(record.InstalledBepInEx, reread.InstalledBepInEx);
        Assert.Equal(record.Files, reread.Files);
    }

    [Fact]
    public void Existing_files_are_never_overwritten()
    {
        using var game = new FakeGame();
        File.WriteAllText(Path.Combine(game.Root, "changelog.txt"), "the game's own changelog");
        var (zip, sha) = FakeBepInExZip();

        var record = new BepInExInstaller(sha).Install(new GameInstall(game.Root, null), zip, FakeDumperDir());

        Assert.Equal("the game's own changelog", File.ReadAllText(Path.Combine(game.Root, "changelog.txt")));
        Assert.DoesNotContain("changelog.txt", record.Files);
    }

    [Fact]
    public void Zip_entries_escaping_the_game_folder_are_rejected()
    {
        using var game = new FakeGame();
        var (zip, sha) = FakeBepInExZip("../evil.dll");

        var ex = Assert.Throws<PkException>(() => new BepInExInstaller(sha).Install(new GameInstall(game.Root, null), zip, FakeDumperDir()));

        Assert.Equal(PkErrorCode.DumperInstallFailed, ex.Code);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(game.Root)!, "evil.dll")));
    }

    [Fact]
    public void Wrong_checksum_is_rejected_before_touching_the_game()
    {
        using var game = new FakeGame();
        var (zip, _) = FakeBepInExZip();

        var ex = Assert.Throws<PkException>(() => new BepInExInstaller("00").Install(new GameInstall(game.Root, null), zip, FakeDumperDir()));

        Assert.Equal(PkErrorCode.DumperInstallFailed, ex.Code);
        Assert.False(File.Exists(Path.Combine(game.Root, "winhttp.dll")));
    }

    [Fact]
    public void Existing_bepinex_is_kept_and_only_the_plugin_added()
    {
        using var game = new FakeGame();
        Directory.CreateDirectory(Path.Combine(game.Root, "BepInEx", "core"));
        File.WriteAllText(Path.Combine(game.Root, "BepInEx", "core", "BepInEx.dll"), "theirs");
        File.WriteAllText(Path.Combine(game.Root, "winhttp.dll"), "theirs");
        var install = new GameInstall(game.Root, null);
        Assert.Equal(InstallState.BepInExOnly, BepInExInstaller.GetState(install));
        var (zip, sha) = FakeBepInExZip();

        var record = new BepInExInstaller(sha).Install(install, zip, FakeDumperDir());

        Assert.False(record.InstalledBepInEx);
        Assert.Equal("theirs", File.ReadAllText(Path.Combine(game.Root, "winhttp.dll")));
        Assert.All(record.Files, f => Assert.StartsWith("BepInEx/plugins/PKModStudio/", f));
    }

    [Fact]
    public void Stray_proxy_dll_without_bepinex_is_a_conflict()
    {
        using var game = new FakeGame();
        File.WriteAllText(Path.Combine(game.Root, "winhttp.dll"), "some other mod loader");
        var install = new GameInstall(game.Root, null);
        var (zip, sha) = FakeBepInExZip();

        Assert.Equal(InstallState.Conflict, BepInExInstaller.GetState(install));
        Assert.Equal(PkErrorCode.BepInExConflict, Assert.Throws<PkException>(() => new BepInExInstaller(sha).Install(install, zip, FakeDumperDir())).Code);
    }

    [Fact]
    public void Uninstall_restores_the_game_folder()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var before = Directory.GetFileSystemEntries(game.Root, "*", SearchOption.AllDirectories).Order().ToList();
        var (zip, sha) = FakeBepInExZip();
        var installer = new BepInExInstaller(sha);
        installer.Install(install, zip, FakeDumperDir());
        Directory.CreateDirectory(Path.Combine(game.Root, "BepInEx", "config"));
        File.WriteAllText(Path.Combine(game.Root, "BepInEx", "LogOutput.log"), "runtime log"); // created by BepInEx at runtime

        var result = installer.Uninstall(install);

        Assert.True(result.RemovedBepInEx);
        Assert.Equal(before, Directory.GetFileSystemEntries(game.Root, "*", SearchOption.AllDirectories).Order().ToList());
        Assert.Equal(InstallState.NotInstalled, BepInExInstaller.GetState(install));
    }

    [Fact]
    public void Uninstall_keeps_bepinex_when_other_plugins_were_added()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var (zip, sha) = FakeBepInExZip();
        var installer = new BepInExInstaller(sha);
        installer.Install(install, zip, FakeDumperDir());
        File.WriteAllText(Path.Combine(game.Root, "BepInEx", "plugins", "SomeoneElsesMod.dll"), "mod");

        var result = installer.Uninstall(install);

        Assert.False(result.RemovedBepInEx);
        Assert.NotNull(result.Note);
        Assert.True(File.Exists(Path.Combine(game.Root, "BepInEx", "plugins", "SomeoneElsesMod.dll")));
        Assert.False(Directory.Exists(BepInExInstaller.PluginDir(install)));
    }

    [Fact]
    public void Uninstall_without_install_reports_not_installed()
    {
        using var game = new FakeGame();
        Assert.Equal(PkErrorCode.DumperNotInstalled,
            Assert.Throws<PkException>(() => new BepInExInstaller().Uninstall(new GameInstall(game.Root, null))).Code);
    }
}
