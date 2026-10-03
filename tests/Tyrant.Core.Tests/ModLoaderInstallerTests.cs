using System.IO.Compression;
using System.Security.Cryptography;
using Tyrant.Core.Dumping;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;

namespace Tyrant.Core.Tests;

public class ModLoaderInstallerTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));

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
        File.WriteAllText(Path.Combine(dir, "Tyrant.Dumper.dll"), "mod");
        File.WriteAllText(Path.Combine(dir, "Tyrant.Dumper.Serialization.dll"), "library");
        File.WriteAllText(Path.Combine(dir, "Tyrant.Framework.dll"), "framework");
        File.WriteAllText(Path.Combine(dir, "Tyrant.Framework.Core.dll"), "framework library");
        return dir;
    }

    /// <summary>A broken build: the dumper without the framework.</summary>
    private static string DumperOnlyDir()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Tyrant.Dumper.dll"), "mod");
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
        Assert.True(File.Exists(Path.Combine(game.Root, "Mods", "Tyrant.Dumper.dll")));
        Assert.True(File.Exists(Path.Combine(game.Root, "UserLibs", "Tyrant.Dumper.Serialization.dll")));
        Assert.Contains("version.dll", record.Files);
        Assert.Contains("Mods/Tyrant.Dumper.dll", record.Files);
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

        var ex = Assert.Throws<TyrantException>(() => new ModLoaderInstaller(sha).Install(new GameInstall(game.Root, null), zip, FakeDumperDir()));

        Assert.Equal(TyrantErrorCode.DumperInstallFailed, ex.Code);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(game.Root)!, "evil.dll")));
        Assert.False(File.Exists(Path.Combine(game.Root, "version.dll")));
    }

    [Fact]
    public void Wrong_checksum_is_rejected_before_touching_the_game()
    {
        using var game = new FakeGame();
        var (zip, _) = FakeMelonLoaderZip();

        var ex = Assert.Throws<TyrantException>(() => new ModLoaderInstaller("00").Install(new GameInstall(game.Root, null), zip, FakeDumperDir()));

        Assert.Equal(TyrantErrorCode.DumperInstallFailed, ex.Code);
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
        Assert.Equal(TyrantErrorCode.ModLoaderConflict,
            Assert.Throws<TyrantException>(() => new ModLoaderInstaller(sha).Install(install, zip, FakeDumperDir())).Code);
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
        Assert.False(File.Exists(Path.Combine(game.Root, "Mods", "Tyrant.Dumper.dll")));
        Assert.False(File.Exists(Path.Combine(game.Root, "UserLibs", "Tyrant.Dumper.Serialization.dll")));
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
        Assert.False(File.Exists(Path.Combine(game.Root, "Mods", "Tyrant.Dumper.dll")));
        Assert.Equal(InstallState.LoaderOnly, ModLoaderInstaller.GetState(install));
    }

    [Fact]
    public void Uninstall_without_install_reports_not_installed()
    {
        using var game = new FakeGame();
        Assert.Equal(TyrantErrorCode.DumperNotInstalled,
            Assert.Throws<TyrantException>(() => new ModLoaderInstaller().Uninstall(new GameInstall(game.Root, null))).Code);
    }

    private static List<string> Snapshot(string root) => Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Order().ToList();

    [Fact]
    public void Failed_install_rolls_back_everything_it_added()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var before = Snapshot(game.Root);
        var blocker = Path.Combine(game.Root, "MelonLoader", "net472", "MelonLoader.dll");
        Directory.CreateDirectory(blocker); // a folder where a file must be extracted: extraction fails midway
        var (zip, sha) = FakeMelonLoaderZip();

        var ex = Assert.Throws<TyrantException>(() => new ModLoaderInstaller(sha).Install(install, zip, FakeDumperDir()));

        Assert.Equal(TyrantErrorCode.DumperInstallFailed, ex.Code);
        Assert.False(File.Exists(Path.Combine(game.Root, "version.dll")));
        Assert.Equal(InstallState.NotInstalled, ModLoaderInstaller.GetState(install));
        Directory.Delete(Path.Combine(game.Root, "MelonLoader"), recursive: true); // the blocker was ours, not the installer's
        Assert.Equal(before, Snapshot(game.Root));
    }

    [Fact]
    public void Failed_uninstall_keeps_the_record_so_it_can_be_retried()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var before = Snapshot(game.Root);
        var (zip, sha) = FakeMelonLoaderZip();
        var installer = new ModLoaderInstaller(sha);
        installer.Install(install, zip, FakeDumperDir());
        var log = Path.Combine(game.Root, "MelonLoader", "Latest.log");
        File.WriteAllText(log, "held open by the game");

        using (new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(TyrantErrorCode.DumperInstallFailed, Assert.Throws<TyrantException>(() => installer.Uninstall(install)).Code);
            Assert.Equal(InstallState.Installed, ModLoaderInstaller.GetState(install));
        }

        Assert.True(installer.Uninstall(install).RemovedLoader);
        Assert.Equal(before, Snapshot(game.Root));
    }

    [Fact]
    public void Install_and_uninstall_refuse_while_the_game_is_running()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var (zip, sha) = FakeMelonLoaderZip();
        var running = new ModLoaderInstaller(sha, _ => true);

        var installEx = Assert.Throws<TyrantException>(() => running.Install(install, zip, FakeDumperDir()));
        new ModLoaderInstaller(sha).Install(install, zip, FakeDumperDir());
        var uninstallEx = Assert.Throws<TyrantException>(() => running.Uninstall(install));

        Assert.Contains("close the game", installEx.Message);
        Assert.Contains("close the game", uninstallEx.Message);
        Assert.Equal(InstallState.Installed, ModLoaderInstaller.GetState(install));
    }

    [Fact]
    public void Damaged_record_with_unsafe_paths_removes_nothing()
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var (zip, sha) = FakeMelonLoaderZip();
        var installer = new ModLoaderInstaller(sha);
        installer.Install(install, zip, FakeDumperDir());
        var victim = Path.Combine(Path.GetDirectoryName(game.Root)!, "Victim");
        Directory.CreateDirectory(victim);
        File.WriteAllText(Path.Combine(victim, "precious.txt"), "keep me");
        File.WriteAllText(Path.Combine(ModLoaderInstaller.RecordDir(install), "install.json"),
            """{"installedLoader":true,"files":["version.dll"],"createdDirs":["../Victim"]}""");

        var ex = Assert.Throws<TyrantException>(() => installer.Uninstall(install));

        Assert.Equal(TyrantErrorCode.DumperInstallFailed, ex.Code);
        Assert.True(File.Exists(Path.Combine(victim, "precious.txt")));
        Assert.True(File.Exists(Path.Combine(game.Root, "version.dll")));
    }

    [Theory]
    [InlineData("UserData", "OtherMod.cfg")]
    [InlineData("UserLibs", "OtherLibrary.dll")]
    public void Leftovers_from_other_mods_keep_melonloader(string folder, string file)
    {
        using var game = new FakeGame();
        var install = new GameInstall(game.Root, null);
        var (zip, sha) = FakeMelonLoaderZip();
        var installer = new ModLoaderInstaller(sha);
        installer.Install(install, zip, FakeDumperDir());
        File.WriteAllText(Path.Combine(game.Root, "UserData", "MelonPreferences.cfg"), "MelonLoader's own");
        File.WriteAllText(Path.Combine(game.Root, folder, file), "someone else's");

        var result = installer.Uninstall(install);

        Assert.False(result.RemovedLoader);
        Assert.NotNull(result.Note);
        Assert.True(File.Exists(Path.Combine(game.Root, folder, file)));
        Assert.False(File.Exists(Path.Combine(game.Root, "Mods", "Tyrant.Dumper.dll")));
    }

    [Fact]
    public void Uninstall_on_an_existing_loader_removes_the_request_and_empty_folders_it_created()
    {
        using var game = new FakeGame();
        FakeExistingMelonLoader(game);
        var install = new GameInstall(game.Root, null);
        var before = Snapshot(game.Root);
        var (zip, sha) = FakeMelonLoaderZip();
        var installer = new ModLoaderInstaller(sha);
        installer.Install(install, zip, FakeDumperDir());
        File.WriteAllText(ModLoaderInstaller.RequestPath(install), "{}"); // left behind by an interrupted dump

        installer.Uninstall(install);

        Assert.Equal(before, Snapshot(game.Root));
    }

    [Fact]
    public void Summaries_describe_what_happened()
    {
        var record = new InstallRecord(true, ["version.dll", "Mods/Tyrant.Dumper.dll"], []);

        Assert.Contains("Installed MelonLoader 0.7.3", ModLoaderInstaller.InstallSummary(InstallState.NotInstalled, record));
        Assert.Contains("2 files", ModLoaderInstaller.InstallSummary(InstallState.NotInstalled, record));
        Assert.Contains("existing MelonLoader", ModLoaderInstaller.InstallSummary(InstallState.LoaderOnly, record));
        Assert.Contains("already installed", ModLoaderInstaller.InstallSummary(InstallState.Installed, record));
        Assert.Contains("dumper and framework", ModLoaderInstaller.InstallSummary(InstallState.NotInstalled, record));
        Assert.Contains("back to vanilla", ModLoaderInstaller.UninstallSummary(new UninstallResult(true, null)));
        Assert.Contains("installed mods", ModLoaderInstaller.UninstallSummary(new UninstallResult(true, null)));
        Assert.Equal("Removed Tyrant's dumper, framework and installed mods. MelonLoader was kept because other mods have files in Mods.",
            ModLoaderInstaller.UninstallSummary(new UninstallResult(false, "MelonLoader was kept because other mods have files in Mods.")));
    }

    private static string FakeGameModsDir()
    {
        var dir = FakeDumperDir();
        File.WriteAllText(Path.Combine(dir, "Tyrant.Framework.dll"), "framework");
        File.WriteAllText(Path.Combine(dir, "Tyrant.Framework.Core.dll"), "framework library");
        return dir;
    }

    [Fact]
    public void Install_copies_the_framework_next_to_the_dumper()
    {
        using var game = new FakeGame();
        var (zip, sha) = FakeMelonLoaderZip();
        var install = new GameInstall(game.Root, null);

        var record = new ModLoaderInstaller(sha).Install(install, zip, FakeGameModsDir());

        Assert.True(File.Exists(Path.Combine(game.Root, "Mods", "Tyrant.Framework.dll")));
        Assert.True(File.Exists(Path.Combine(game.Root, "UserLibs", "Tyrant.Framework.Core.dll")));
        Assert.Contains("Mods/Tyrant.Framework.dll", record.Files);
        Assert.True(ModLoaderInstaller.HasFramework(install));
    }

    [Fact]
    public void Installing_again_adds_the_framework_to_an_existing_install()
    {
        using var game = new FakeGame();
        var (zip, sha) = FakeMelonLoaderZip();
        var install = new GameInstall(game.Root, null);
        new ModLoaderInstaller(sha).Install(install, zip, FakeGameModsDir());
        File.Delete(Path.Combine(game.Root, "Mods", "Tyrant.Framework.dll")); // what the user had before Plan 8: no framework in the game
        File.Delete(Path.Combine(game.Root, "UserLibs", "Tyrant.Framework.Core.dll"));
        Assert.False(ModLoaderInstaller.HasFramework(install));

        var record = new ModLoaderInstaller(sha).Install(install, null, FakeGameModsDir());

        Assert.True(ModLoaderInstaller.HasFramework(install));
        Assert.Contains("Mods/Tyrant.Dumper.dll", record.Files);
        Assert.Contains("UserLibs/Tyrant.Framework.Core.dll", record.Files);
    }

    [Fact]
    public void Uninstall_removes_the_framework_and_installed_mods()
    {
        using var game = new FakeGame();
        var (zip, sha) = FakeMelonLoaderZip();
        var install = new GameInstall(game.Root, null);
        new ModLoaderInstaller(sha).Install(install, zip, FakeGameModsDir());
        var mod = Path.Combine(ModLoaderInstaller.ModsDir(install), "red-spot");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, "mod.json"), "{}");

        var result = new ModLoaderInstaller(sha).Uninstall(install);

        Assert.True(result.RemovedLoader); // our framework and mods are not "other mods' files"
        Assert.False(Directory.Exists(Path.Combine(game.Root, "Mods")));
        Assert.False(Directory.Exists(Path.Combine(game.Root, "UserData")));
        Assert.False(ModLoaderInstaller.HasFramework(install));
    }

    [Fact]
    public void Framework_status_compares_the_shipped_and_installed_files()
    {
        using var game = new FakeGame();
        var (zip, sha) = FakeMelonLoaderZip();
        var install = new GameInstall(game.Root, null);
        var shipped = FakeGameModsDir();
        Assert.Equal(FrameworkState.Missing, ModLoaderInstaller.FrameworkStatus(install, shipped));

        new ModLoaderInstaller(sha).Install(install, zip, shipped);
        var current = ModLoaderInstaller.FrameworkStatus(install, shipped);
        File.WriteAllText(Path.Combine(shipped, "Tyrant.Framework.dll"), "framework v2"); // a newer Tyrant ships a different DLL
        var outdated = ModLoaderInstaller.FrameworkStatus(install, shipped);

        Assert.Equal((FrameworkState.Current, FrameworkState.Outdated), (current, outdated));
    }

    [Fact]
    public void Install_refuses_a_build_without_the_framework()
    {
        using var game = new FakeGame();
        var (zip, sha) = FakeMelonLoaderZip();

        var ex = Assert.Throws<TyrantException>(() => new ModLoaderInstaller(sha).Install(new GameInstall(game.Root, null), zip, DumperOnlyDir()));

        Assert.Contains("Tyrant.Framework.dll", ex.Message);
        Assert.False(File.Exists(Path.Combine(game.Root, "version.dll"))); // nothing was installed
    }
}
