using AssetsTools.NET.Extra;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Jobs;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Mods;

/// <summary>Stages a prefab's serialized-file dependency closure for local Unity project recovery.</summary>
public static class UnityPrefabImport
{
    private const string HelperVersion = "2.0.0";
    private const string HelperHash = "9A7EF0E7C5C3EA5B90B4E6D855E2D98D5F7EC8C3F9E26FCCBC194C6A7B01BAF7";

    public static UnityPrefabImportResult Run(Workspace workspace, GameInstall install, AssetIndex index,
        AssetRecord prefab, string project, string speciesId, string displayName,
        IProgress<JobProgress>? progress = null, CancellationToken cancellationToken = default) =>
        RunAsync(workspace, install, index, prefab, project, speciesId, displayName, progress, cancellationToken).GetAwaiter().GetResult();

    private static async Task<UnityPrefabImportResult> RunAsync(Workspace workspace, GameInstall install, AssetIndex index,
        AssetRecord prefab, string project, string speciesId, string displayName,
        IProgress<JobProgress>? progress, CancellationToken ct)
    {
        if (File.Exists(Path.Combine(project, "Temp", "UnityLockfile")))
            throw new TyrantException(TyrantErrorCode.ModInvalid, "Close this Unity project before importing its template.");
        Workspace.EnsureOutsideGame(project, install.RootDir);
        UnityAuthoringReferences.Prepare(project, install.ManagedDir);
        var run = Path.Combine(workspace.CacheDir, "unity-import", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        progress?.Report(new JobProgress(0.05, "Collecting prefab dependencies"));
        Stage(install, index, prefab, Path.Combine(run, "input"), ct);
        progress?.Report(new JobProgress(0.15, "Preparing AssetRipper " + HelperVersion));
        var zip = Path.Combine(workspace.CacheDir, "downloads", "AssetRipper-" + HelperVersion + "-win-x64.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromMinutes(15) };
        if (!File.Exists(zip) || await Hash(zip, ct) != HelperHash)
        {
            // GitHub release downloads redirect to its release asset CDN.
            using var downloader = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            var temporary = zip + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var output = File.Create(temporary))
                await using (var input = await downloader.GetStreamAsync("https://github.com/AssetRipper/AssetRipper/releases/download/" + HelperVersion + "/AssetRipper_win_x64.zip", ct))
                    await input.CopyToAsync(output, ct);
                if (await Hash(temporary, ct) != HelperHash) throw new InvalidDataException("The AssetRipper download does not match the pinned official release.");
                File.Move(temporary, zip, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        var helper = Path.Combine(run, "helper");
        ZipFile.ExtractToDirectory(zip, helper);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var start = new ProcessStartInfo(Path.Combine(helper, "AssetRipper.GUI.Free.exe"))
        {
            WorkingDirectory = run, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "--headless", "--port", port.ToString(), "--log=false" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start AssetRipper.");
        await using var stdout = File.Create(Path.Combine(run, "helper-output.log"));
        await using var stderr = File.Create(Path.Combine(run, "helper-errors.log"));
        var outputTask = process.StandardOutput.BaseStream.CopyToAsync(stdout);
        var errorTask = process.StandardError.BaseStream.CopyToAsync(stderr);
        var completed = false;
        try
        {
            client.BaseAddress = new Uri("http://127.0.0.1:" + port);
            var ready = false;
            var timer = Stopwatch.StartNew();
            while (!ready && timer.Elapsed < TimeSpan.FromSeconds(30))
            {
                ct.ThrowIfCancellationRequested();
                if (process.HasExited) throw new InvalidOperationException("AssetRipper exited. See " + run);
                try { using var response = await client.GetAsync("/", ct); ready = response.IsSuccessStatusCode; }
                catch (HttpRequestException) { await Task.Delay(100, ct); }
            }
            if (!ready) throw new TimeoutException("AssetRipper did not start. See " + run);
            await Post("/Settings/Update", new Dictionary<string, string>
            {
                ["ScriptExportMode"] = "DllExportWithoutRenaming", ["ShaderExportMode"] = "Dummy",
                ["ScriptContentLevel"] = "Level2", ["BundledAssetsExportMode"] = "DirectExport",
                ["DefaultVersion"] = "2022.3.62f3", ["TargetVersion"] = "2022.3.62f3",
            });
            progress?.Report(new JobProgress(0.3, "Recovering PK prefab and components"));
            await Post("/LoadFolder", new Dictionary<string, string> { ["Path"] = Path.Combine(run, "input") });
            progress?.Report(new JobProgress(0.6, "Exporting editable Unity assets"));
            await Post("/Export/UnityProject", new Dictionary<string, string> { ["Path"] = Path.Combine(run, "export") });
            progress?.Report(new JobProgress(0.85, "Binding local PK scripts and importing dependencies"));
            var result = UnityRecoveredAssets.Import(Path.Combine(run, "export", "ExportedProject", "Assets"), project,
                prefab.ContainerPath!, speciesId, displayName, ct);
            completed = true;
            return result;
        }
        catch (Exception ex) when (ex is not TyrantException and not OperationCanceledException)
        {
            throw new TyrantException(TyrantErrorCode.AssetUnreadable, "Prefab import failed: " + ex.Message + " Logs: " + run, FixAction.None, ex);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(outputTask, errorTask);
            if (completed)
            {
                foreach (var name in new[] { "input", "export", "helper" })
                {
                    // Only temporary children of this call's unique cache folder are removed.
                    var temporary = Path.GetFullPath(Path.Combine(run, name));
                    if (!temporary.StartsWith(Path.GetFullPath(run) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Import cache path escaped its run directory.");
                    try { Directory.Delete(temporary, true); }
                    catch (IOException) { /* Keep diagnostics if another process still holds a generated file. */ }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        async Task Post(string endpoint, Dictionary<string, string> form)
        {
            using var body = new FormUrlEncodedContent(form);
            using var response = await client.PostAsync(endpoint, body, ct);
            if (response.StatusCode != HttpStatusCode.Found) response.EnsureSuccessStatusCode();
        }
    }

    private static async Task<string> Hash(string file, CancellationToken ct)
    {
        await using var stream = File.OpenRead(file);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    public static IReadOnlyList<string> Stage(GameInstall install, AssetIndex index, AssetRecord prefab, string destination,
        CancellationToken cancellationToken = default)
    {
        if (prefab.IsBuiltIn || prefab.Type != "GameObject" || prefab.ContainerPath is null)
            throw new TyrantException(TyrantErrorCode.ModInvalid, "Choose an Addressables animal prefab.");
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new TyrantException(TyrantErrorCode.ModInvalid, "The import staging folder must be empty.");
        Directory.CreateDirectory(destination);
        using var session = new AssetSession(install);
        var manager = new AssetsManager();
        var pending = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var copied = new List<string>();
        pending.Enqueue(prefab.Bundle);
        try
        {
            while (pending.TryDequeue(out var relative))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!visited.Add(relative)) continue;
                var source = session.BundlePath(relative);
                var bundle = manager.LoadBundleFile(source, true);
                for (var i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
                {
                    if (!bundle.file.IsAssetsFile(i)) continue;
                    var file = manager.LoadAssetsFileFromBundle(bundle, i, false);
                    foreach (var external in file.file.Metadata.Externals)
                    {
                        var dependency = index.BundleOfArchive(external.PathName);
                        if (dependency is not null) pending.Enqueue(dependency);
                        else if (!external.PathName.EndsWith("unity default resources", StringComparison.OrdinalIgnoreCase)
                                 && !external.PathName.EndsWith("unity_builtin_extra", StringComparison.OrdinalIgnoreCase))
                            throw new TyrantException(TyrantErrorCode.AssetNotFound, "Unresolved prefab dependency: " + external.PathName);
                    }
                }
                var target = Path.Combine(destination, Path.GetFileName(source));
                if (File.Exists(target)) throw new InvalidDataException("Duplicate dependency bundle filename: " + target);
                File.Copy(source, target);
                copied.Add(target);
                manager.UnloadAllAssetsFiles();
                manager.UnloadAllBundleFiles();
            }
            var managed = Path.Combine(destination, "Managed");
            Directory.CreateDirectory(managed);
            foreach (var source in Directory.EnumerateFiles(install.ManagedDir, "*.dll"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Copy(source, Path.Combine(managed, Path.GetFileName(source)));
            }
            return copied;
        }
        finally { manager.UnloadAll(true); }
    }
}
