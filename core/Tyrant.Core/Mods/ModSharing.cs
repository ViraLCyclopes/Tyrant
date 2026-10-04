using System.IO.Compression;
using System.Text;
using Tyrant.Core.Dumping;
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
