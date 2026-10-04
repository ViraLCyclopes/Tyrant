using System.IO.Compression;
using System.Text;
using Tyrant.Core.Errors;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Dumping;

/// <summary>Tyrant's framework as a zip for players without Tyrant: unzip into the game folder after installing MelonLoader.</summary>
public static class FrameworkPackage
{
    public static string FileName => $"Tyrant-Framework-{FrameworkInfo.Version}.zip";

    public static string Write(string componentDir, string zipPath)
    {
        var layout = ModLoaderInstaller.FrameworkLayout(componentDir);
        if (!layout.Any(f => f.Relative == "Mods/" + ModLoaderInstaller.FrameworkFile))
            throw new TyrantException(TyrantErrorCode.DumperInstallFailed, $"{ModLoaderInstaller.FrameworkFile} is missing from '{componentDir}'. Rebuild Tyrant.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        var temp = zipPath + ".writing";
        if (File.Exists(temp)) File.Delete(temp);
        using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (var (source, relative) in layout) archive.CreateEntryFromFile(source, relative, CompressionLevel.Optimal);
            using var readme = new StreamWriter(archive.CreateEntry("README.txt").Open(), new UTF8Encoding(false));
            readme.Write($"""
                Tyrant Framework {FrameworkInfo.Version} for Prehistoric Kingdom

                It loads mods made with Tyrant. It never replaces game files.

                Install:
                1. Install MelonLoader {ModLoaderInstaller.Version} (https://github.com/LavaGang/MelonLoader/releases).
                2. Unzip this file into the game folder (Mods\ and UserLibs\ go next to the game's exe).
                3. Put mods in UserData\Tyrant\Mods\<mod id> (a mod's zip does that when unzipped into the game folder).
                   Mods load in the order of UserData\Tyrant\mods.json; mods not listed there load after, by id.

                Remove: delete Mods\Tyrant.Framework.dll and UserLibs\Tyrant.Framework*.dll.
                """.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n"));
        }
        File.Move(temp, zipPath, overwrite: true);
        return zipPath;
    }
}
