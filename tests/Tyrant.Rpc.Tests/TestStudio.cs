using Tyrant.Core.Tests;
using Tyrant.Core.Assets;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Tyrant.Core.Dumping;
using Tyrant.Core.Install;
using Tyrant.Dumper.Serialization;
using Tyrant.Rpc.Studio;

namespace Tyrant.Rpc.Tests;

/// <summary>Synthetic Studio services: no Steam, no downloads, no real game.</summary>
public static class TestStudio
{
    public static string TempDir() => Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws");

    public sealed class NoSteam : ISteamRootProvider
    {
        public string? GetSteamRoot() => null;
    }

    public sealed class NeverLauncher : IGameLauncher
    {
        public bool IsRunning(GameInstall install) => false;
        public void Launch(GameInstall install) { }
    }

    public static StudioOptions Options(IGameLauncher? launcher = null, (string Path, string Sha)? loaderZip = null, IAssetReader? reader = null, string? dumperDir = null,
        Func<HttpClient>? http = null, Tyrant.Core.Blender.BlenderEnvironment? blender = null) => new()
    {
        Blender = blender ?? NoBlender(),
        Http = http ?? (() => throw new InvalidOperationException("This test has no network.")),
        Locator = new GameInstallLocator(new NoSteam()),
        Launcher = launcher ?? new NeverLauncher(),
        DumperDir = dumperDir ?? FakeDumperDir(),
        LoaderZip = (_, _) => loaderZip?.Path ?? throw new InvalidOperationException("This test has no MelonLoader archive."),
        Installer = () => new ModLoaderInstaller(loaderZip?.Sha ?? ModLoaderInstaller.Sha256, _ => false),
        AssetReader = reader ?? new FakeAssetReader(),
    };

    public sealed class SilentLink(bool answers = false) : Tyrant.Core.Blender.IBlenderLink
    {
        public List<string> Opened { get; } = [];

        public bool TryOpen(string projectFile)
        {
            Opened.Add(projectFile);
            return answers;
        }
    }

    /// <summary>No Blender anywhere unless given: an empty Program Files, no Steam, a link nobody answers.</summary>
    public static Tyrant.Core.Blender.BlenderEnvironment NoBlender(FakeBlenderProcess? process = null, Tyrant.Core.Blender.IBlenderLink? link = null,
        string? programFiles = null, string? appData = null) => new()
    {
        Process = process ?? new FakeBlenderProcess(),
        Link = link ?? new SilentLink(),
        Steam = new NoSteam(),
        ProgramFiles = programFiles ?? Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"))).FullName,
        AppData = appData ?? Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"))).FullName,
        AddonZip = Path.Combine(Path.GetTempPath(), "tyrant-tests", "no-addon.zip"),
        TyrantExe = "tyrant.exe",
    };

    /// <summary>A zip shaped like the MelonLoader release; returns (path, sha256).</summary>
    public static (string Path, string Sha) FakeMelonLoaderZip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "MelonLoader.x64.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var entry in new[] { "version.dll", "MelonLoader/net35/MelonLoader.dll", "MelonLoader/net472/MelonLoader.dll" })
            {
                using var w = new StreamWriter(zip.CreateEntry(entry).Open());
                w.Write("fake " + entry);
            }
        }
        using var stream = File.OpenRead(path);
        return (path, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }

    public static string FakeDumperDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Tyrant.Dumper.dll"), "mod");
        File.WriteAllText(Path.Combine(dir, "Tyrant.Dumper.Serialization.dll"), "library");
        File.WriteAllText(Path.Combine(dir, "Tyrant.Framework.dll"), "framework");
        File.WriteAllText(Path.Combine(dir, "Tyrant.Framework.Core.dll"), "framework library");
        return dir;
    }

    /// <summary>The CLI's dumper/ folder as Task 3 builds it: dumper plus framework.</summary>
    public static string FakeGameModsDir()
    {
        var dir = FakeDumperDir();
        File.WriteAllText(Path.Combine(dir, "Tyrant.Framework.dll"), "framework");
        File.WriteAllText(Path.Combine(dir, "Tyrant.Framework.Core.dll"), "framework library");
        return dir;
    }
}

public class FakeDumpLauncher(int objects) : IGameLauncher
{
    public bool IsRunning(GameInstall install) => false;

    public virtual void Launch(GameInstall install)
    {
        var request = JsonDocument.Parse(File.ReadAllText(DumpRunner.RequestPath(install))).RootElement;
        var result = new DumpResult();
        for (var i = 0; i < objects; i++)
            result.Objects.Add(new DumpObject(typeof(object), new EngineObjectInfo("PrehistoricKingdom.AnimalData", $"Animal{i}", i), "{}"));
        DumpWriter.Write(request.GetProperty("outputDir").GetString()!, result, [],
            new DumpManifest { RequestId = request.GetProperty("requestId").GetString()!, BuildGuid = "b" });
    }
}

/// <summary>Like <see cref="FakeDumpLauncher"/> but waits in Launch until released, so a job stays running.</summary>
public sealed class BlockingDumpLauncher(int objects) : FakeDumpLauncher(objects)
{
    public ManualResetEventSlim Entered { get; } = new();
    public ManualResetEventSlim Release { get; } = new();

    public override void Launch(GameInstall install)
    {
        Entered.Set();
        Release.Wait(TimeSpan.FromSeconds(30));
        base.Launch(install);
    }
}
