using System.Security.Cryptography;
using StbImageSharp;
using StbImageWriteSharp;
using Tyrant.Core.Assets;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>
/// What the editor's iridescence preview needs: the numbers and the ramp and mask as PNG files (null mask: everywhere).
/// <c>Numbers["_Smoothness"]</c>, when present, multiplies the extra map's smoothness (the mod's value over the game's).
/// </summary>
public sealed record MaterialPreviewResult(bool On, float Strength, float Scale, float Power, float Bias, string? RampFile, string? MaskFile, float[] MaskSt,
    IReadOnlyDictionary<string, float> Numbers);

/// <summary>Layers and previews of a material's settings, from this mod's own settings and, for textures and numbers, the game's.</summary>
public static class MaterialPreview
{
    private const string Species = "species";
    private const string StartingSkin = "starting skin";

    /// <summary>
    /// The layers under the one being edited (a skin: its starting skin, then its species; a vanilla skin
    /// (<paramref name="vanillaSkin"/>, "&lt;species&gt;/&lt;skin&gt;"): its species; a species: none), merged, and which layer
    /// each set field comes from (setting key, or "iridescence.&lt;field&gt;"). Base game values are not part of it.
    /// </summary>
    public static (MaterialOverride? Below, IReadOnlyDictionary<string, string> Sources) Inherited(ModProject mod, string? skinId, string? speciesId, string material,
        string? vanillaSkin = null)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        MaterialOverride? starting = null, species;
        if (skinId is not null)
        {
            var skin = mod.Skin(skinId);
            if (StartingSkins.InheritsFrom(mod.Manifest, skin, out var layer) && layer.Materials is { } blocks && blocks.TryGetValue(material, out var found)) starting = found;
            species = mod.SpeciesMaterial(speciesId ?? skin.Species, material);
        }
        else if (vanillaSkin is not null && vanillaSkin.IndexOf('/') is > 0 and var slash) species = mod.SpeciesMaterial(vanillaSkin[..slash], material);
        else return (null, sources);
        foreach (var (name, layerOverride) in new[] { (StartingSkin, starting), (Species, species) })
        {
            if (layerOverride is null) continue;
            foreach (var key in layerOverride.Settings.Keys) sources.TryAdd(key, name);
            if (layerOverride.Iridescence is { } i)
                foreach (var field in SetFields(i)) sources.TryAdd("iridescence." + field, name);
        }
        return (MaterialTable.Merge(starting, species), sources);
    }

    /// <summary>
    /// The preview of <paramref name="draft"/> (the settings being edited) over this mod's lower layers and the game's material.
    /// Files are written under <paramref name="outDir"/> named by their content; <paramref name="exportTexture"/> gives a game texture as a PNG.
    /// <paramref name="gameStrength"/> is the vanilla skin's own iridescence for this sex (<see cref="VanillaSkin.Iridescence"/>),
    /// used when no layer sets a strength; null when unknown (none). <paramref name="vanillaMap"/> gives the game's map in a slot,
    /// for this sex, as a PNG file (null: none), for masks taken from one: a vanilla skin's (<paramref name="vanillaSkin"/>) own, or a
    /// mod skin's base skin's for a slot the mod has no PNG for (the framework does the same in the game).
    /// </summary>
    public static MaterialPreviewResult Build(ModProject mod, string? skinId, string? speciesId, string material, MaterialOverride? draft, string sex,
        AnimalMaterial? game, string outDir, Func<AssetRecord, string?> exportTexture, float? gameStrength = null,
        string? vanillaSkin = null, Func<string, string?>? vanillaMap = null)
    {
        var merged = MaterialTable.Merge(draft, Inherited(mod, skinId, speciesId, material, vanillaSkin).Below);
        var i = merged?.Iridescence;
        var strength = i?.Strength?.Normal ?? gameStrength ?? 0f;
        var numbers = new Dictionary<string, float>(StringComparer.Ordinal);
        // _Smoothness: a multiplier on the extra map's smoothness (R), the mod's value over the game's, so a material without
        // one keeps the map's per-pixel smoothness; only when the draft or a mod layer sets it.
        if (merged is not null && merged.Settings.TryGetValue("_Smoothness", out var s) && s.Kind == MaterialValueKind.Number)
            numbers["_Smoothness"] = game is not null && game.Floats.TryGetValue("_Smoothness", out var g) && g > 0 ? s.Number / g : s.Number;

        string? rampFile = null, maskFile = null;
        // "game" is the game material's own ramp and mask, over any lower layer's.
        if (i?.Ramp is { Game: false } ramp)
        {
            if (ramp.File is not null) rampFile = ModFile(mod, ramp.File);
            else if (IridescenceRamp.Stops(ramp) is { Count: > 0 } stops) rampFile = WritePng(outDir, IridescenceRamp.Pixels(stops), 32, 32);
        }
        else if (game?.Ramp is { } gameRamp) rampFile = exportTexture(gameRamp);

        if (i?.Mask is { Game: false } mask)
        {
            if (mask.File is not null) maskFile = ModFile(mod, mask.File);
            else if (mask.From is not null
                     && (skinId is not null ? SlotImage(mod, skinId, sex, mask.From) ?? Decode(vanillaMap?.Invoke(mask.From))
                         : vanillaSkin is not null ? Decode(vanillaMap?.Invoke(mask.From)) : null) is { } slot)
                maskFile = WritePng(outDir, MaskChannel.Extract(slot.Data, mask.Channel, mask.Invert), slot.Width, slot.Height);
        }
        else if (game?.Mask is { } gameMask) maskFile = exportTexture(gameMask);

        var on = strength > 0 && rampFile is not null;
        return new MaterialPreviewResult(on, strength, i?.Scale ?? Game(game, "_IridescenceScale", 1), i?.Power ?? Game(game, "_IridescencePower", 1),
            i?.Bias ?? Game(game, "_IridescenceBias", 0), on ? rampFile : null, on ? maskFile : null,
            [i?.Tiling?[0] ?? 1, i?.Tiling?[1] ?? 1, i?.Offset?[0] ?? 0, i?.Offset?[1] ?? 0], numbers);
    }

    private static float Game(AnimalMaterial? game, string key, float fallback) => game is not null && game.Floats.TryGetValue(key, out var v) ? v : fallback;

    private static IEnumerable<string> SetFields(IridescenceSettings i)
    {
        if (i.Strength != null) yield return "strength";
        if (i.Variation != null) yield return "variation";
        if (i.Ramp != null) yield return "ramp";
        if (i.Mask != null) yield return "mask";
        if (i.Tiling != null) yield return "tiling";
        if (i.Offset != null) yield return "offset";
        if (i.Scale != null) yield return "scale";
        if (i.Power != null) yield return "power";
        if (i.Bias != null) yield return "bias";
    }

    private static string? ModFile(ModProject mod, string file)
    {
        var path = Path.GetFullPath(Path.Combine(mod.Dir, file));
        return ModPaths.IsInside(path, mod.Dir) && File.Exists(path) ? path : null;
    }

    /// <summary>The skin's PNG for a slot and sex, else its starting skin's (which it inherits).</summary>
    private static ImageResult? SlotImage(ModProject mod, string skinId, string sex, string slot)
    {
        var skin = mod.Skin(skinId);
        var files = new List<SkinEntry> { skin };
        if (StartingSkins.InheritsFrom(mod.Manifest, skin, out var starting)) files.Add(starting);
        foreach (var entry in files)
        {
            var own = sex == "female" ? entry.Female : entry.Male;
            if (own is null || !own.TryGetValue(slot, out var file) || ModFile(mod, file) is not { } path) continue;
            return Decode(path);
        }
        return null;
    }

    private static ImageResult? Decode(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
        {
            return null;
        }
    }

    private static string WritePng(string outDir, byte[] rgba, int width, int height)
    {
        Directory.CreateDirectory(outDir);
        var hash = Convert.ToHexString(SHA256.HashData(rgba))[..16].ToLowerInvariant();
        var path = Path.Combine(outDir, $"material-{width}x{height}-{hash}.png");
        if (File.Exists(path)) return path;
        // Written aside and moved in whole: a file under the final name is always complete, so a later preview may reuse it.
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".writing";
        try
        {
            using (var stream = File.Create(temp))
                new ImageWriter().WritePng(rgba, width, height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException) when (File.Exists(path))
        {
            // another preview wrote the same image (same name, same content) and has it open
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        return path;
    }
}
