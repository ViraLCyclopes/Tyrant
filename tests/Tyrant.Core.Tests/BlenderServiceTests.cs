using Tyrant.Core.Assets;
using Tyrant.Core.Blender;
using Tyrant.Core.Install;
using Tyrant.Core.Models;
using Tyrant.Core.Mods;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Tests;

public class BlenderServiceTests
{
    private sealed class Link(bool answers) : IBlenderLink
    {
        public List<string> Opened { get; } = [];

        public bool TryOpen(string projectFile)
        {
            Opened.Add(projectFile);
            return answers;
        }
    }

    private sealed class NoSteam : ISteamRootProvider
    {
        public string? GetSteamRoot() => null;
    }

    private static string Temp() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"))).FullName;

    private static (BlenderEnvironment Env, FakeBlenderProcess Process, Link Link, string Exe) Env(bool running, string version = "Blender 5.2.2", string? installed = "0.1.0")
    {
        var programFiles = Temp();
        var dir = Directory.CreateDirectory(Path.Combine(programFiles, "Blender Foundation", "Blender 5.2")).FullName;
        var exe = Path.Combine(dir, "blender.exe");
        File.WriteAllText(exe, "");
        var process = new FakeBlenderProcess();
        process.Versions[exe] = version;
        var appData = Temp();
        if (installed is not null)
        {
            var minor = BlenderLocator.ParseVersion(version)!;
            var addon = Directory.CreateDirectory(Path.Combine(appData, "Blender Foundation", "Blender", $"{minor.Major}.{minor.Minor}", "extensions", "user_default", "tyrant_blender")).FullName;
            File.WriteAllText(Path.Combine(addon, "blender_manifest.toml"), $"version = \"{installed}\"");
        }
        var zip = Path.Combine(Temp(), "tyrant_blender.zip");
        using (var a = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
        using (var w = new StreamWriter(a.CreateEntry("blender_manifest.toml").Open())) w.Write("version = \"0.1.0\"");
        var link = new Link(running);
        var env = new BlenderEnvironment
        {
            Process = process, Link = link, Steam = new NoSteam(), ProgramFiles = programFiles, AppData = appData, AddonZip = zip, TyrantExe = @"C:\T\tyrant.exe",
        };
        return (env, process, link, exe);
    }

    private static Workspace Ws(out FakeGame game)
    {
        game = new FakeGame();
        var ws = Workspace.Create(Path.Combine(Temp(), "ws"), new GameInstall(game.Root, null));
        SkinDumps.Write(ws.DataDir);
        return ws;
    }

    private static (GameInstall Install, AssetIndex Index, IReadOnlyList<SpeciesSkins> Species, FakeAssetReader Reader, PrefabModel Prefab) Game(Workspace ws, FakeGame game)
    {
        var prefab = ModelFixture.Prefab(ModelFixture.Triangle(name: "Carch_LOD0"));
        prefab = prefab with { Renderers = [prefab.Renderers[0] with { Materials = [new MaterialModel("Carch", [])] }] };
        return (new GameInstall(game.Root, null), new AssetIndex { Assets = [.. SkinDumps.Textures, SkinDumps.Prefab] }, SpeciesSkinsReader.Load(ws),
            new FakeAssetReader { PrefabModelToReturn = prefab }, prefab);
    }

    [Fact]
    public void Status_reports_blender_and_the_addon()
    {
        var (env, _, _, exe) = Env(running: false);
        var ws = Ws(out var game);
        using var _ = game;

        var s = new BlenderService(env).Status(ws);

        Assert.True(s.Found);
        Assert.Equal(exe, s.Exe);
        Assert.Equal("5.2.2", s.Version);
        Assert.True(s.Supported);
        Assert.Equal("current", s.Addon);
        Assert.Null(s.Problem);
    }

    [Fact]
    public void Blender_4_is_found_but_not_supported()
    {
        var (env, _, _, _) = Env(running: false, version: "Blender 4.5.0");
        var ws = Ws(out var game);
        using var _ = game;

        var s = new BlenderService(env).Status(ws);

        Assert.False(s.Supported);
        Assert.Contains("5.0 or newer", s.Problem);
    }

    [Fact]
    public void Set_path_saves_it_in_the_workspace_and_rejects_non_blender()
    {
        var (env, _, _, exe) = Env(running: false);
        var ws = Ws(out var game);
        using var _ = game;
        var service = new BlenderService(env);

        service.SetPath(ws, exe);
        Assert.Equal(exe, Workspace.Open(ws.Dir).Data.BlenderPath);

        var notBlender = Path.Combine(Temp(), "notepad.exe");
        File.WriteAllText(notBlender, "");
        Assert.Throws<Tyrant.Core.Errors.TyrantException>(() => service.SetPath(ws, notBlender));
        service.SetPath(ws, null);
        Assert.Null(Workspace.Open(ws.Dir).Data.BlenderPath);
    }

    [Fact]
    public void Install_runs_the_extension_installer_and_reports_the_new_state()
    {
        var (env, process, _, _) = Env(running: false, installed: null);
        var ws = Ws(out var game);
        using var _ = game;

        new BlenderService(env).InstallAddon(ws);

        Assert.Contains(process.Runs, r => r.Args.Contains("install-file") && r.Args.Contains(env.AddonZip));
    }

    [Fact]
    public void Open_uses_a_running_blender_else_starts_one()
    {
        var ws = Ws(out var game);
        using var _ = game;
        var (install, index, species, reader, _) = Game(ws, game);

        var (envRunning, processRunning, linkRunning, _) = Env(running: true);
        var running = new BlenderService(envRunning).Open(ws, install, index, species, reader, new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false));
        Assert.Equal("running", running.How);
        Assert.Single(linkRunning.Opened);
        Assert.Empty(processRunning.Starts);

        var (envIdle, processIdle, _, exe) = Env(running: false);
        var started = new BlenderService(envIdle).Open(ws, install, index, species, reader, new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false));
        Assert.Equal("started", started.How);
        var start = Assert.Single(processIdle.Starts);
        Assert.Equal(exe, start.Exe);
        Assert.Equal("--python-expr", start.Args[0]);
        Assert.Contains(started.ProjectFile, start.Args[1]);
    }

    [Fact]
    public void Open_without_the_addon_says_how_to_install_it()
    {
        var (env, _, _, _) = Env(running: false, installed: null);
        var ws = Ws(out var game);
        using var _ = game;
        var (install, index, species, reader, _) = Game(ws, game);

        var ex = Assert.Throws<Tyrant.Core.Errors.TyrantException>(() =>
            new BlenderService(env).Open(ws, install, index, species, reader, new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false)));

        Assert.Contains("Install add-on", ex.Message);
        Assert.Contains("tyrant blender install-addon", ex.Message);
    }

    [Fact]
    public void Send_without_a_destination_asks_for_one_and_a_failed_build_reports_errors()
    {
        var ws = Ws(out var game);
        using var _ = game;
        var (install, index, species, reader, prefab) = Game(ws, game);
        var project = BlenderProjectWriter.Write(new BlenderOpenRequest("Carcharodontosaurus", null, null, false, false), ws, install, index, species, reader, "t.exe");
        var glb = Path.Combine(project.Dir, "send.glb");
        GltfModelWriter.WriteGlb(prefab, prefab.Renderers[0], glb, [new GltfMaterial("Carch")]);

        var none = BlenderService.Send(ws, install, index, species, reader, project.ProjectFile, glb, null, null);
        Assert.False(none.Ok);
        Assert.Contains("Choose where to send it", none.Errors[0]);

        var sent = BlenderService.Send(ws, install, index, species, reader, project.ProjectFile, glb, new BlenderDestination("new-carch", "Carcharodontosaurus", null), "New Carch");
        Assert.True(sent.Ok, string.Join(" ", sent.Errors));
        Assert.Single(ModProject.Open(ws, "new-carch").Manifest.Models);
        Assert.Equal("new-carch", BlenderProjectFile.Read(project.ProjectFile).Destination!.Mod);

        var badGlb = Path.Combine(project.Dir, "bad.glb");
        File.WriteAllText(badGlb, "not a glb");
        var failed = BlenderService.Send(ws, install, index, species, reader, project.ProjectFile, badGlb, null, null);
        Assert.False(failed.Ok);
        Assert.NotEmpty(failed.Errors);
    }

    [Fact]
    public void Destinations_list_mods_with_that_species_skins()
    {
        var ws = Ws(out var game);
        using var _ = game;
        ModProject.Create(ws, "mod-a", "Mod A", null);

        var choices = BlenderService.Destinations(ws, "Carcharodontosaurus");

        var a = Assert.Single(choices);
        Assert.Equal("Mod A", a.Name);
        Assert.Empty(a.Skins);
    }
}
