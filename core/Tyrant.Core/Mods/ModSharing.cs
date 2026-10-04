using System.IO.Compression;
using System.Text;
using Tyrant.Core.Dumping;
using Tyrant.Core.Errors;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>Mods as zips to share: Export writes one laid out for the game folder; Import brings one into the workspace.</summary>
public static class ModSharing
{
    /// <summary>Where a mod sits inside an exported zip (and in the game folder).</summary>
    public const string ZipRoot = "UserData/Tyrant/Mods/";

    /// <summary>mod.json and every file it references that exists inside the mod, as sorted forward-slash paths.</summary>
    public static IReadOnlyList<string> SharedFiles(ModProject mod)
    {
        var m = mod.Manifest;
        var named = new List<string?> { ModManifest.FileName, m.Assembly };
        named.AddRange(m.Replace.Select(r => r.File));
        foreach (var skin in m.Skins)
        {
            named.Add(skin.Thumbnail);
            named.AddRange(skin.Male?.Values ?? Enumerable.Empty<string>());
            named.AddRange(skin.Female?.Values ?? Enumerable.Empty<string>());
            named.AddRange(ModelFilesOf(mod, skin.Model));
        }
        foreach (var model in m.Models) named.AddRange(ModelFilesOf(mod, model.File));
        return named.Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!.Replace('\\', '/'))
            .Where(f => ModPaths.IsInside(Path.Combine(mod.Dir, f), mod.Dir) && File.Exists(Path.Combine(mod.Dir, f)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A model's .glb with its built LODs and report (ModelFiles: x.lodN.tmesh, x.model.json).</summary>
    private static IEnumerable<string> ModelFilesOf(ModProject mod, string? glb)
    {
        if (string.IsNullOrWhiteSpace(glb)) yield break;
        yield return glb;
        yield return ModelFiles.Report(glb);
        for (var lod = 0; File.Exists(Path.Combine(mod.Dir, ModelFiles.Lod(glb, lod))); lod++) yield return ModelFiles.Lod(glb, lod);
    }

    public static string Export(ModProject mod, string zipPath, string frameworkVersion)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        var temp = zipPath + ".writing";
        if (File.Exists(temp)) File.Delete(temp);
        using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (var file in SharedFiles(mod))
                archive.CreateEntryFromFile(Path.Combine(mod.Dir, file), ZipRoot + mod.Id + "/" + file, CompressionLevel.Optimal);
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
    public static ModProject Import(Workspace ws, string zipPath, bool replace)
    {
        var zipName = Path.GetFileName(zipPath);
        using var archive = ZipFile.OpenRead(zipPath);
        var files = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name))
            .Select(e => (Entry: e, Name: e.FullName.Replace('\\', '/').TrimStart('/'))).ToList();
        if (files.Any(f => f.Name.Split('/').Any(part => part == "..") || Path.IsPathRooted(f.Name) || f.Name.Contains(':')))
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{zipName}' contains an unsafe path; nothing was imported.");

        var manifests = files.Where(f => f.Name == ModManifest.FileName || f.Name.EndsWith("/" + ModManifest.FileName, StringComparison.Ordinal)).ToList();
        if (manifests.Count != 1)
            throw new TyrantException(TyrantErrorCode.ModInvalid, manifests.Count == 0
                ? $"'{zipName}' has no mod.json, so it is not a Tyrant mod."
                : $"'{zipName}' holds {manifests.Count} mods; import one zip per mod.");
        var prefix = manifests[0].Name[..^ModManifest.FileName.Length];

        ModManifest manifest;
        using (var reader = new StreamReader(manifests[0].Entry.Open()))
        {
            try { manifest = ModManifest.Parse(reader.ReadToEnd()); }
            catch (ManifestException ex) { throw new TyrantException(TyrantErrorCode.ModInvalid, $"The mod in '{zipName}' cannot be read: {ex.Message}"); }
        }
        var folder = prefix.TrimEnd('/').Split('/').Last();
        if (!ModId.IsValid(manifest.Id) || (folder.Length > 0 && folder != manifest.Id))
            throw new TyrantException(TyrantErrorCode.ModInvalid,
                $"The mod's id \"{manifest.Id}\" {(ModId.IsValid(manifest.Id) ? $"does not match its folder \"{folder}\"" : "is not a valid mod id")}; nothing was imported.");

        var target = Path.Combine(ModProject.RootOf(ws), manifest.Id);
        if (Directory.Exists(target) && !replace)
            throw new TyrantException(TyrantErrorCode.ModExists,
                $"This workspace already has a mod '{manifest.Id}'. Import again and confirm to replace it (Mods tab), or add --replace.");

        var staging = target + ".importing";
        var previous = target + ".previous";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        try
        {
            foreach (var (entry, name) in files.Where(f => f.Name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                var destination = Path.GetFullPath(Path.Combine(staging, name[prefix.Length..]));
                if (!ModPaths.IsInside(destination, staging))
                    throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{zipName}' contains an unsafe path; nothing was imported.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination);
            }
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
        return ModProject.Open(ws, manifest.Id);
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
            2. Install Tyrant Framework: unzip Tyrant-Framework-{frameworkVersion}.zip into the game folder, or use Tyrant
               (Workspace tab -> Install Tyrant in game).
            3. Unzip this file into the game folder. The mod lands in UserData\Tyrant\Mods\{m.Id}.

            Remove: delete the folder UserData\Tyrant\Mods\{m.Id}.
            """.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
    }
}
