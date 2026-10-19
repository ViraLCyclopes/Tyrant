using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Tyrant.Core.ModelReplacements;
using Tyrant.Core.Dumping;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Textures;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>An imported mod, how many of its PNGs were extracted from its cooked pack, and the PNGs still missing.</summary>
public sealed record ImportedMod(ModProject Mod, int Extracted, IReadOnlyList<string> Warnings);

/// <summary>Mods as zips to share: Export writes one laid out for the game folder; Import brings one into the workspace.</summary>
public static class ModSharing
{
    /// <summary>A file of a mod being brought in: its '/' path in the zip or folder, and how to read it.</summary>
    private sealed record SourceFile(string Name, Func<Stream> Open);

    /// <summary>Where a mod sits inside an exported zip (and in the game folder).</summary>
    public const string ZipRoot = "UserData/Tyrant/Mods/";

    /// <summary>mod.json and every file it or the material folders reference that exists inside the mod, as sorted forward-slash paths.</summary>
    public static IReadOnlyList<string> SharedFiles(ModProject mod)
    {
        var m = mod.Manifest;
        foreach (var species in m.Species.Where(s => s.Prefab != null))
            if (!File.Exists(Path.Combine(mod.Dir, species.Prefab!.Bundle)))
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"Species '{species.Id}' is missing its prefab bundle '{species.Prefab.Bundle}'.");
        var named = new List<string?> { ModManifest.FileName, m.Assembly };
        if (File.Exists(mod.DatabasePath)) named.Add(AnimalDatabaseFile.FileName);
        foreach (var code in AnimalLocalization.Codes) named.Add("localization/" + code + ".json");
        foreach (var code in AnimalLocalization.Codes) named.Add("localization/" + code + ".txt");
        foreach (var entry in m.Species)
            foreach (var term in entry.Localization.Values)
                foreach (var code in AnimalLocalization.Codes) named.Add($"localization/{code}/{term.Replace('\\', '/')}.txt");
        named.AddRange(mod.NamedFiles());
        named.AddRange(mod.PrefabFiles().Keys.Select(f => PrefabOverrides.Folder + "/" + f));
        return named.Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!.Replace('\\', '/'))
            .Where(f => ModPaths.IsInside(Path.Combine(mod.Dir, f), mod.Dir) && File.Exists(Path.Combine(mod.Dir, f)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A model's .glb with its built LODs and report (ModelFiles: x.lodN.tmesh, x.model.json).</summary>
    internal static IEnumerable<string> ModelFilesOf(ModProject mod, string? glb)
    {
        if (string.IsNullOrWhiteSpace(glb)) yield break;
        yield return glb;
        yield return ModelFiles.Report(glb);
        for (var lod = 0; File.Exists(Path.Combine(mod.Dir, ModelFiles.Lod(glb, lod))); lod++) yield return ModelFiles.Lod(glb, lod);
    }

    /// <param name="cook">Writes the mod's textures.tpack into the given temp folder and returns its path (or null when there is nothing to cook).</param>
    /// <param name="includePngs">False: the zip leaves out the PNGs the pack holds, like an install (<see cref="SlimCopy"/>).</param>
    public static string Export(ModProject mod, string zipPath, string frameworkVersion, Func<string, string?>? cook = null, bool includePngs = true)
    {
        var packDir = Path.Combine(Path.GetTempPath(), "tyrant-pack-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pack = cook?.Invoke(packDir);
            SlimPlan? slim = null;
            if (pack is not null && !includePngs)
                using (var stream = File.OpenRead(pack)) slim = SlimCopy.Plan(mod.Manifest, mod.Dir, TexturePack.Read(stream));
            return WriteZip(mod, zipPath, frameworkVersion, pack, slim);
        }
        finally
        {
            try { if (Directory.Exists(packDir)) Directory.Delete(packDir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string WriteZip(ModProject mod, string zipPath, string frameworkVersion, string? pack, SlimPlan? slim)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        var temp = zipPath + ".writing";
        if (File.Exists(temp)) File.Delete(temp);
        using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (var file in SharedFiles(mod).Where(f => slim is null || !slim.Dropped.Contains(f)))
            {
                var name = ZipRoot + mod.Id + "/" + file;
                var source = Path.Combine(mod.Dir, file);
                if (file == ModManifest.FileName) WriteText(archive, name, SharedManifest(slim?.Manifest ?? mod.Manifest, frameworkVersion));
                else if (file.EndsWith(".model.json", StringComparison.OrdinalIgnoreCase)) WriteText(archive, name, WithoutLocalPaths(File.ReadAllText(source)));
                else if (file == AnimalDatabaseFile.FileName)
                {
                    var snapshot = Path.Combine(Path.GetTempPath(), "tyrant-animal-snapshot-" + Guid.NewGuid().ToString("N") + ".db");
                    try
                    {
                        AnimalDatabaseFile.CopySnapshot(source, snapshot);
                        archive.CreateEntryFromFile(snapshot, name, CompressionLevel.Optimal);
                    }
                    finally { if (File.Exists(snapshot)) File.Delete(snapshot); }
                }
                else archive.CreateEntryFromFile(source, name, CompressionLevel.Optimal);
            }
            foreach (var (thumbnail, png) in slim?.Thumbnails ?? new Dictionary<string, byte[]>())
                using (var entry = archive.CreateEntry(ZipRoot + mod.Id + "/" + thumbnail, CompressionLevel.Optimal).Open())
                    entry.Write(png);
            if (pack is not null) archive.CreateEntryFromFile(pack, ZipRoot + mod.Id + "/" + TexturePack.FileName, CompressionLevel.Fastest);
            using var readme = new StreamWriter(archive.CreateEntry("README.txt").Open(), new UTF8Encoding(false));
            readme.Write(Readme(mod, frameworkVersion));
        }
        File.Move(temp, zipPath, overwrite: true);
        return zipPath;
    }

    /// <summary>
    /// Brings a shared mod into the workspace (mods/&lt;id&gt;): an Export zip (UserData/Tyrant/Mods/&lt;id&gt;/…), a zip of the
    /// mod folder, or mod.json at the zip's root. Unsafe paths, no mod or several refuse the whole zip; an existing mod is
    /// replaced only with <paramref name="replace"/>, and only once the new one has unpacked.
    /// </summary>
    public static ModProject Import(Workspace ws, string zipPath, bool replace) => ImportReported(ws, zipPath, replace).Mod;

    /// <summary><see cref="Import"/>, also saying how many PNGs came from the cooked pack and which are still missing.</summary>
    public static ImportedMod ImportReported(Workspace ws, string zipPath, bool replace)
    {
        var zipName = Path.GetFileName(zipPath);
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var files = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name))
                .Select(e => new SourceFile(e.FullName.Replace('\\', '/').TrimStart('/'), e.Open)).ToList();
            return Unpack(ws, files, zipName, replace);
        }
        catch (InvalidDataException ex)
        {
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{zipName}' is damaged or not a zip ({ex.Message}); nothing was imported.");
        }
    }

    /// <summary>
    /// A mod only in the game folder (UserData/Tyrant/Mods/&lt;id&gt;) brought into the workspace like an import: a slim
    /// install's PNGs are extracted from its pack.
    /// </summary>
    public static ImportedMod CopyFromGame(Workspace ws, GameInstall install, string id, bool replace)
    {
        var root = Path.GetFullPath(ModLoaderInstaller.ModsDir(install));
        var plain = !string.IsNullOrWhiteSpace(id) && id is not ("." or "..") && Path.GetFileName(id) == id && id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        var dir = plain ? Path.Combine(root, id) : null;
        if (dir is null || !Directory.Exists(dir))
            throw new TyrantException(TyrantErrorCode.TargetNotFound, $"'{id}' is not in the game's mods folder.");
        var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => new SourceFile(Path.GetRelativePath(dir, f).Replace('\\', '/'), () => File.OpenRead(f))).ToList();
        var imported = Unpack(ws, files, id, replace);
        // The workspace now matches the game's copy: stamped as if installed, so a later in-game save shows on the game's side only.
        if (imported.Mod.Id == id)
        {
            try
            {
                GameLooks.Stamp(install, id, dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return imported with { Warnings = [.. imported.Warnings, $"The game's looks of '{id}' could not be recorded ({ex.Message}); later changes in the game may show as changed on both sides."] };
            }
        }
        return imported;
    }

    private static ImportedMod Unpack(Workspace ws, List<SourceFile> files, string zipName, bool replace)
    {
        if (files.Any(f => f.Name.Split('/').Any(part => part == "..") || Path.IsPathRooted(f.Name) || f.Name.Contains(':')))
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{zipName}' contains an unsafe path; nothing was imported.");

        var manifests = files.Where(f => f.Name == ModManifest.FileName || f.Name.EndsWith("/" + ModManifest.FileName, StringComparison.Ordinal)).ToList();
        if (manifests.Count != 1)
            throw new TyrantException(TyrantErrorCode.ModInvalid, manifests.Count == 0
                ? $"'{zipName}' has no mod.json, so it is not a Tyrant mod."
                : $"'{zipName}' holds {manifests.Count} mods; import one zip per mod.");
        var prefix = manifests[0].Name[..^ModManifest.FileName.Length];

        ModManifest manifest;
        using (var reader = new StreamReader(manifests[0].Open()))
        {
            try { manifest = ModManifest.Parse(reader.ReadToEnd()); }
            catch (ManifestException ex) { throw new TyrantException(TyrantErrorCode.ModInvalid, $"The mod in '{zipName}' cannot be read: {ex.Message}"); }
        }
        // The mod lands under mod.json's id whatever its folder in the zip is called (a renamed folder, GitHub's <repo>-main).
        if (!ModId.IsValid(manifest.Id))
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"The mod's id \"{manifest.Id}\" is not a valid mod id; nothing was imported.");

        var target = Path.Combine(ModProject.RootOf(ws), manifest.Id);
        if (Directory.Exists(target) && !replace)
            throw new TyrantException(TyrantErrorCode.ModExists,
                $"This workspace already has a mod '{manifest.Id}'. Import again and confirm to replace it (Mods tab), or add --replace.");

        var staging = target + ".importing";
        var previous = target + ".previous";
        var extracted = 0;
        var warnings = new List<string>();
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        try
        {
            foreach (var file in files.Where(f => f.Name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                var destination = Path.GetFullPath(Path.Combine(staging, file.Name[prefix.Length..]));
                if (!ModPaths.IsInside(destination, staging))
                    throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{zipName}' contains an unsafe path; nothing was imported.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var input = file.Open();
                using var output = File.Create(destination);
                input.CopyTo(output);
            }
            // The material folders before Restore: the masks they name come back from the pack too.
            var problems = new List<string>();
            MaterialFolders.Load(manifest, staging, problems);
            if (problems.Count > 0)
                throw new TyrantException(TyrantErrorCode.ModInvalid, $"The mod in '{zipName}' cannot be read: {string.Join(" ", problems)}");
            (extracted, warnings) = Restore(staging, manifest, ModTextureCooker.CacheDirOf(ws));
            // Validate the database before replacing an existing workspace mod.
            try { AnimalDatabaseFile.Read(Path.Combine(staging, AnimalDatabaseFile.FileName)); }
            catch (ManifestException ex) { throw new TyrantException(TyrantErrorCode.ModInvalid, "The imported animal database is invalid: " + ex.Message); }
            if (Directory.Exists(previous)) Directory.Delete(previous, recursive: true);
            if (Directory.Exists(target)) Directory.Move(target, previous);
            try
            {
                Directory.Move(staging, target);
            }
            catch
            {
                if (Directory.Exists(previous) && !Directory.Exists(target)) Directory.Move(previous, target);
                throw;
            }
            if (Directory.Exists(previous)) Directory.Delete(previous, recursive: true);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
        var imported = ModProject.Open(ws, manifest.Id);
        Restamp(imported);
        return new ImportedMod(imported, extracted, warnings);
    }

    /// <summary>
    /// A slim mod made editable: every PNG mod.json's cooked slots name that is missing is extracted from textures.tpack;
    /// auto thumbnails go; the pack goes (Cook textures and install rebuild it). Returns the count and one warning per PNG
    /// still missing. Each extracted texture's cooked data goes into the cook cache (<paramref name="cacheDir"/>), so
    /// installing the mod again reuses it rather than compressing it twice.
    /// </summary>
    private static (int Extracted, List<string> Warnings) Restore(string staging, ModManifest manifest, string cacheDir)
    {
        var missing = ModTextureCooker.TextureFiles(manifest)
            .Where(f => ModTextureCooker.InsidePath(staging, f) is { } p && !File.Exists(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var packPath = Path.Combine(staging, TexturePack.FileName);
        var extracted = 0;
        if (missing.Count > 0 && File.Exists(packPath))
        {
            try
            {
                var written = TexturePackExtractor.ExtractTo(packPath, staging, missing.Contains).Written;
                extracted = written.Count;
                using var stream = File.OpenRead(packPath);
                foreach (var entry in TexturePack.Read(stream).Entries.Where(e => written.Contains(TexturePack.NormalSource(e.Source), StringComparer.OrdinalIgnoreCase)))
                    ModTextureCooker.Seed(cacheDir, Path.Combine(staging, TexturePack.NormalSource(entry.Source)), stream, entry);
            }
            catch (TyrantException) { } // a damaged pack: every PNG stays missing and is reported below
        }
        if (File.Exists(packPath)) File.Delete(packPath);
        var warnings = missing.Where(f => !File.Exists(Path.Combine(staging, f))).Select(f => $"{f} is not in the zip or its cooked pack").ToList();
        if (SlimCopy.WithoutAutoThumbnails(manifest))
        {
            foreach (var file in Directory.GetFiles(staging, "*.png", SearchOption.AllDirectories).Where(f => SlimCopy.IsAutoThumbnail(f)))
                File.Delete(file);
            File.WriteAllText(Path.Combine(staging, ModManifest.FileName), manifest.ToJson());
        }
        return (extracted, warnings);
    }

    /// <summary>Where players get Tyrant and its framework zip.</summary>
    public const string ReleasesUrl = "https://github.com/ViraLCyclopes/Tyrant/releases";

    /// <summary>The players' guide to installing MelonLoader, the framework and mods (docs/installing-mods.md).</summary>
    public const string GuideUrl = "https://github.com/ViraLCyclopes/Tyrant/blob/main/docs/installing-mods.md";

    private static void WriteText(ZipArchive archive, string name, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    /// <summary>mod.json as shared: it names the framework it needs, so an older framework skips it with a message.</summary>
    private static string SharedManifest(ModManifest manifest, string frameworkVersion)
    {
        var copy = ModManifest.Parse(manifest.ToJson());
        // At least the exporting framework: an older stamp (a mod imported from an older zip) may predate fields the mod now uses.
        if (copy.RequiresTyrant is null || Tyrant.Core.Updates.SemVer.Compare(copy.RequiresTyrant, frameworkVersion) < 0)
            copy.RequiresTyrant = frameworkVersion;
        return copy.ToJson();
    }

    /// <summary>A model report without where the modder's own .glb lives on their PC (Tyrant's Re-import uses it; players don't).</summary>
    private static string WithoutLocalPaths(string reportJson)
    {
        try
        {
            if (JsonNode.Parse(reportJson) is not JsonObject report) return reportJson;
            report.Remove("origin");
            report.Remove("originStamp");
            return report.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch (System.Text.Json.JsonException)
        {
            return "{}"; // unreadable: Check builds it again; never ship what could not be cleaned
        }
    }

    /// <summary>Unzipping restores rounded file times: stamp each model report against its .glb as it is now, so it is not "changed".</summary>
    private static void Restamp(ModProject mod)
    {
        var glbs = mod.Manifest.Models.Select(m => m.File).Concat(mod.Manifest.Skins.Select(s => s.Model)).Where(f => !string.IsNullOrWhiteSpace(f));
        foreach (var glb in glbs)
        {
            var glbPath = Path.Combine(mod.Dir, glb!);
            var reportPath = Path.Combine(mod.Dir, ModelFiles.Report(glb!));
            if (!File.Exists(glbPath) || !File.Exists(reportPath)) continue;
            try
            {
                if (JsonNode.Parse(File.ReadAllText(reportPath)) is not JsonObject report) continue;
                report["stamp"] = ModelBuilder.Stamp(glbPath);
                File.WriteAllText(reportPath, report.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch (System.Text.Json.JsonException)
            {
                // left as it is: Check reports it as out of date and Install rebuilds it
            }
        }
    }

    private static string Readme(ModProject mod, string frameworkVersion)
    {
        var m = mod.Manifest;
        return $"""
            {m.Name} ({m.Id}) {m.Version}{(string.IsNullOrWhiteSpace(m.Author) ? "" : " by " + m.Author)}
            {(string.IsNullOrWhiteSpace(m.Description) ? "" : m.Description + "\r\n")}
            A Prehistoric Kingdom mod made with Tyrant. It never replaces game files.

            Needs: MelonLoader {ModLoaderInstaller.Version} and Tyrant Framework {frameworkVersion} or newer.

            Install:
            1. Install MelonLoader {ModLoaderInstaller.Version} (https://github.com/LavaGang/MelonLoader/releases).
            2. Install Tyrant Framework: unzip Tyrant-Framework-{frameworkVersion}.zip (from {ReleasesUrl})
               into the game folder, or use Tyrant (Workspace tab -> Install Tyrant in game).
            3. Unzip this file into the game folder. The mod lands in UserData\Tyrant\Mods\{m.Id}.

            Updating: delete the folder UserData\Tyrant\Mods\{m.Id} first, then unzip the new version
            (texture files left from the old one would be shown instead of the new ones).

            Remove: delete the folder UserData\Tyrant\Mods\{m.Id}.

            Step-by-step guide (with or without the Tyrant app): {GuideUrl}
            """.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
    }
}
