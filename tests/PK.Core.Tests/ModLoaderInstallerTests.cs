using System.IO.Compression;
using System.Security.Cryptography;
using PK.Core.Dumping;
using PK.Core.Errors;
using PK.Core.Install;

namespace PK.Core.Tests;

public class ModLoaderInstallerTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "pk-tests", Guid.NewGuid().ToString("N"));

    /// <summary>A zip shaped like the MelonLoader release; returns (path, sha256).</summary>
    private static (string Path, string Sha) FakeMelonLoaderZip(params string[] extraEntries)
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "MelonLoader.x64.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var entry in new[] { "version.dll", "MelonLoader/net35/MelonLoader.dll", "MelonLoader/net472/MelonLoader.dll", "MelonLoader/Documentation/README.md" }.Concat(extraEntries))
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
        File.WriteAllText(Path.Combine(dir, "PK.Dumper.dll"), "mod");
        File.WriteAllText(Path.Combine(dir, "PK.Dumper.Serialization.dll"), "library");
        return dir;
    }

    private static void FakeExistingMelonLoader(FakeGame game)
    {
        Directory.CreateDirectory(Path.Combine(game.Root, "MelonLoader", "net35"));
        File.WriteAllText(Path.Combine(game.Root, "MelonLoader", "net35", "MelonLoader.dll"), "theirs");
        File.WriteAllText(Path.Combine(game.Root, "version.dll"), "theirs");
    }

    [Fact]
    public void Install_adds_melonloader_and_the_mod_and_records_them()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var (zip, sha) = FakeMelonLoaderZip();

        var record = new ModLoaderInstaller(sha).Install(install, zip, FakeDumperDir());

        Assert.True(record.InstalledLoader);
        Assert.True(File.Exists(Path.Combine(game.Root, "version.dll")));
        Assert.True(File.Exists(Path.Combine(game.Root, "Mods", "PK.Dumper.dll")));
        Assert.True(File.Exists(Path.Combine(game.Root, "UserLibs", "PK.Dumper.Serialization.dll")));
        Assert.Contains("version.dll", record.Files);
        Assert.Contains("Mods/PK.Dumper.dll", record.Files);
        Assert.Contains("Mods", record.CreatedDirs);
        Assert.Equal(InstallState.Installed, ModLoaderInstaller.GetState(install));
        var reread = ModLoaderInstaller.ReadRecord(install)!;
        Assert.Equal(record.Files, reread.Files);
        Assert.Equal(record.CreatedDirs, reread.CreatedDirs);
    }

    [Fact]
    public void Existing_files_are_never_overwritten()
    {
        using var game = new FakeGame();
        Directory.CreateDirectory(Path.Combine(game.Root, "MelonLoader", "Documentation"));
        File.WriteAllText(Path.Combine(game.Root, "MelonLoader", "Documentation", "README.md"), "already here");
        var (zip, sha) = FakeMelonLoaderZip();

        var record = new ModLoaderInstaller(sha).Install(new GameInstall(game.Root, null), zip, FakeDumperDir());

        Assert.Equal("already here", File.ReadAllText(Path.Combine(game.Root, "MelonLoader", "Documentation", "README.md")));
        Assert.DoesNotContain("MelonLoader/Documentation/README.md", record.Files);
        Assert.DoesNotContain("MelonLoader", record.CreatedDirs);
    }

    [Fact]
    public void Zip_entries_escaping_the_game_folder_are_rejected()
    {
        using var game = new FakeGame();
        var (zip, sha) = FakeMelonLoaderZip("../evil.dll");

        var ex = Assert.Throws<PkException>(() => new ModLoaderInstaller(sha).Install(new GameInstall(game.Root, null), zip, FakeDumperDir()));

        Assert.Equal(PkErrorCode.DumperInstallFailed, ex.Code);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(game.Root)!, "evil.dll")));
        Assert.False(File.Exists(Path.Combine(game.Root, "version.dll")));
    }

    [Fact]
    public void Wrong_checksum_is_rejected_before_touching_the_game()
    {
        using var game = new FakeGame();
        var (zip, _) = FakeMelonLoaderZip();

        var ex = Assert.Throws<PkException>(() => new ModLoaderInstaller("00").Install(new GameInstall(game.Root, null), zip, FakeDumperDir()));

        Assert.Equal(PkErrorCode.DumperInstallFailed, ex.Code);
        Assert.False(File.Exists(Path.Combine(game.Root, "version.dll")));
    }

    [Fact]
    public void Existing_melonloader_is_kept_and_only_the_mod_added()
    {
        using var game = new FakeGame();
        FakeExistingMelonLoader(game);
        var install = new GameInstall(game.Root, null);
        Assert.Equal(InstallState.LoaderOnly, ModLoaderInstaller.GetState(install));
        var (zip, sha) = FakeMelonLoaderZip();

        var record = new ModLoaderInstaller(sha).Install(install, zip, FakeDumperDir());

        Assert.False(record.InstalledLoader);
        Assert.Equal("theirs", File.ReadAllText(Path.Combine(game.Root, "version.dll")));
        Assert.All(record.Files, f => Assert.True(f.StartsWith("Mods/") || f.StartsWith("UserLibs/"), f));
    }

    [Theory]
    [InlineData("bepinex")]
    [InlineData("stray-version-dll")]
    public void Another_loader_or_unknown_proxy_dll_is_a_conflict(string kind)
    {
        using var game = new FakeGame();
        if (kind == "bepinex")
        {
            Directory.CreateDirectory(Path.Combine(game.Root, "BepInEx", "core"));
            File.WriteAllText(Path.Combine(game.Root, "BepInEx", "core", "BepInEx.dll"), "bepinex");
            File.WriteAllText(Path.Combine(game.Root, "winhttp.dll"), "bepinex");
        }
        else
        {
            File.WriteAllText(Path.Combine(game.Root, "version.dll"), "something else");
        }
        var install = new GameInstall(game.Root, null);
        var (zip, sha) = FakeMelonLoaderZip();

        Assert.Equal(InstallState.Conflict, ModLoaderInstaller.GetState(install));
        Assert.Equal(PkErrorCode.ModLoaderConflict,
            Assert.Throws<PkException>(() => new ModLoaderInstaller(sha).Install(install, zip, FakeDumperDir())).Code);
    }

    [Fact]
    public void Uninstall_restores_the_game_folder()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var before = Directory.GetFileSystemEntries(game.Root, "*", SearchOption.AllDirectories).Order().ToList();
        var (zip, sha) = FakeMelonLoaderZip();
        var installer = new ModLoaderInstaller(sha);
        installer.Install(install, zip, FakeDumperDir());
        File.WriteAllText(Path.Combine(game.Root, "MelonLoader", "Latest.log"), "runtime log");          // created by MelonLoader at runtime
        File.WriteAllText(Path.Combine(game.Root, "UserData", "MelonPreferences.cfg"), "runtime config");

        var result = installer.Uninstall(install);

        Assert.True(result.RemovedLoader);
        Assert.Equal(before, Directory.GetFileSystemEntries(game.Root, "*", SearchOption.AllDirectories).Order().ToList());
        Assert.Equal(InstallState.NotInstalled, ModLoaderInstaller.GetState(install));
    }

    [Fact]
    public void Uninstall_keeps_melonloader_when_other_mods_were_added()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var (zip, sha) = FakeMelonLoaderZip();
        var installer = new ModLoaderInstaller(sha);
        installer.Install(install, zip, FakeDumperDir());
        File.WriteAllText(Path.Combine(game.Root, "Mods", "SomeoneElsesMod.dll"), "mod");

        var result = installer.Uninstall(install);

        Assert.False(result.RemovedLoader);
        Assert.NotNull(result.Note);
        Assert.True(File.Exists(Path.Combine(game.Root, "Mods", "SomeoneElsesMod.dll")));
        Assert.False(File.Exists(Path.Combine(game.Root, "Mods", "PK.Dumper.dll")));
        Assert.False(File.Exists(Path.Combine(game.Root, "UserLibs", "PK.Dumper.Serialization.dll")));
        Assert.True(File.Exists(Path.Combine(game.Root, "version.dll")));
    }

    [Fact]
    public void Uninstall_from_an_existing_melonloader_removes_only_the_mod()
    {
        using var game = new FakeGame();
        FakeExistingMelonLoader(game);
        var install = new GameInstall(game.Root, null);
        var (zip, sha) = FakeMelonLoaderZip();
        var installer = new ModLoaderInstaller(sha);
        installer.Install(install, zip, FakeDumperDir());

        var result = installer.Uninstall(install);

        Assert.False(result.RemovedLoader);
        Assert.True(File.Exists(Path.Combine(game.Root, "MelonLoader", "net35", "MelonLoader.dll")));
        Assert.False(File.Exists(Path.Combine(game.Root, "Mods", "PK.Dumper.dll")));
        Assert.Equal(InstallState.LoaderOnly, ModLoaderInstaller.GetState(install));
    }

    [Fact]
    public void Uninstall_without_install_reports_not_installed()
    {
        using var game = new FakeGame();
        Assert.Equal(PkErrorCode.DumperNotInstalled,
            Assert.Throws<PkException>(() => new ModLoaderInstaller().Uninstall(new GameInstall(game.Root, null))).Code);
    }
}
