using Tyrant.Core.Errors;
using Tyrant.Core.Textures;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

public sealed partial class ModProject
{
    /// <summary>
    /// Sets the mod's texture compression (file null; mode required) or one texture's own (mode null clears it). The file
    /// must be one of the mod's skin-slot or replacement textures.
    /// </summary>
    public void SetCompression(string? file, TextureCompression? mode)
    {
        var settings = Manifest.Textures;
        if (file is null)
        {
            settings.Compression = mode ?? throw new TyrantException(TyrantErrorCode.ModInvalid, "Give the mod's compression: auto or bc7.");
        }
        else
        {
            var normal = file.Replace('\\', '/');
            var notTexture = $"'{file}' is not one of the textures of '{Id}' (a skin slot or a texture replacement); thumbnails and portraits are not compressed.";
            var known = ModTextureCooker.TextureFiles(Manifest).FirstOrDefault(f => string.Equals(f, normal, StringComparison.OrdinalIgnoreCase));
            if (known is null && mode is null)
            {
                // Clearing: drop any override for this file, even one left for a file that is no longer a texture (Check reports those).
                var stale = settings.Overrides.Keys.Where(k => string.Equals(k.Replace('\\', '/'), normal, StringComparison.OrdinalIgnoreCase)).ToList();
                if (stale.Count == 0) throw new TyrantException(TyrantErrorCode.TargetNotFound, notTexture);
                foreach (var key in stale) settings.Overrides.Remove(key);
                Save();
                return;
            }
            if (known is null) throw new TyrantException(TyrantErrorCode.TargetNotFound, notTexture);
            settings.Overrides.Remove(known);
            if (mode is { } m) settings.Overrides[known] = m;
        }
        Save();
    }

    /// <summary>Drops overrides for files that are no longer textures of the mod, and those equal to the mod's own setting.</summary>
    internal static void PruneTextureOverrides(ModManifest manifest)
    {
        var settings = manifest.Textures;
        if (settings.Overrides.Count == 0) return;
        var files = new HashSet<string>(ModTextureCooker.TextureFiles(manifest), StringComparer.OrdinalIgnoreCase);
        foreach (var (file, mode) in settings.Overrides.ToList())
            if (!files.Contains(file) || mode == settings.Compression) settings.Overrides.Remove(file);
    }
}
