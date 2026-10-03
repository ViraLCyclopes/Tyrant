using Tyrant.Core.Dumping;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

public sealed record OrphanSkin(string Species, string Key, int Number);

/// <summary>
/// Clean-up of UserData/Tyrant/skin-slots.json (written by the framework in game): lists skins whose mod is gone and frees
/// chosen numbers so new skins can reuse them.
/// </summary>
public sealed class SkinSlots(Func<GameInstall, bool>? isGameRunning = null)
{
    public static string PathOf(GameInstall install) => Path.Combine(ModLoaderInstaller.RecordDir(install), SkinNumbers.FileName);

    /// <summary>Skins whose mod is not installed, or whose installed mod no longer has them (a mod that cannot be read keeps its skins).</summary>
    public IReadOnlyList<OrphanSkin> Orphans(GameInstall install)
    {
        var numbers = Read(install);
        var installed = InstalledSkins(install);
        return numbers.Species
            .SelectMany(species => numbers.Of(species).Select(p => new OrphanSkin(species, p.Key, p.Value)))
            .Where(o => IsOrphan(o.Key, installed))
            .OrderBy(o => o.Species, StringComparer.Ordinal).ThenBy(o => o.Number)
            .ToList();
    }

    public int Forget(GameInstall install, IEnumerable<string> keys)
    {
        if (isGameRunning?.Invoke(install) == true)
            throw new TyrantException(TyrantErrorCode.GameRunning, "Prehistoric Kingdom is running; close the game before cleaning up skin numbers.");
        var numbers = Read(install);
        var forgotten = 0;
        foreach (var key in keys.Distinct(StringComparer.Ordinal))
            foreach (var species in numbers.Species.ToList())
                if (numbers.Forget(species, key)) forgotten++;
        if (forgotten > 0) File.WriteAllText(PathOf(install), numbers.ToJson());
        return forgotten;
    }

    private static SkinNumbers Read(GameInstall install)
    {
        var path = PathOf(install);
        try
        {
            return SkinNumbers.Parse(File.Exists(path) ? File.ReadAllText(path) : null);
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"{SkinNumbers.FileName} in the game's UserData/Tyrant cannot be read: {ex.Message}");
        }
    }

    /// <summary>Installed mod id → its skin ids, or null when its mod.json cannot be read.</summary>
    private static Dictionary<string, HashSet<string>?> InstalledSkins(GameInstall install)
    {
        var result = new Dictionary<string, HashSet<string>?>(StringComparer.Ordinal);
        var root = ModLoaderInstaller.ModsDir(install);
        if (!Directory.Exists(root)) return result;
        foreach (var dir in Directory.GetDirectories(root))
        {
            try
            {
                var manifest = ModManifest.Parse(File.ReadAllText(Path.Combine(dir, ModManifest.FileName)));
                result[Path.GetFileName(dir)] = manifest.Skins.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            }
            catch (Exception ex) when (ex is ManifestException or IOException or UnauthorizedAccessException)
            {
                result[Path.GetFileName(dir)] = null;
            }
        }
        return result;
    }

    private static bool IsOrphan(string key, Dictionary<string, HashSet<string>?> installed)
    {
        var slash = key.IndexOf('/');
        if (slash <= 0) return true;
        return !installed.TryGetValue(key[..slash], out var skins) || (skins is not null && !skins.Contains(key[(slash + 1)..]));
    }
}
