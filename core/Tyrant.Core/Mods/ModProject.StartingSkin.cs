using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>Where a custom species' starting skin gets its PNGs.</summary>
public abstract record StartingSkinSource;

/// <summary>A base-game skin, every map it has (any species: the template's by default).</summary>
public sealed record CopySkin(string SpeciesId, int Skin) : StartingSkinSource;

/// <summary>The modder's diffuse PNGs (female: the male one when null); the other maps come from the template skin.</summary>
public sealed record OwnPngs(string Male, string? Female) : StartingSkinSource;

public sealed partial class ModProject
{
    /// <summary>The starting skin of this mod's species (by its id in the mod), or null.</summary>
    public SkinEntry? StartingSkinOf(string speciesId) =>
        Manifest.Species.FirstOrDefault(s => s.Id == speciesId) is { } entry ? StartingSkins.Of(Manifest, entry.Key(Manifest.Id)) : null;

    /// <summary>A new species with its editable starting skin; when the skin fails, the species is removed again.</summary>
    public SpeciesEntry CreateSpecies(GameInstall install, AssetIndex index, IAssetReader reader, IReadOnlyList<SpeciesSkins> vanilla,
        string id, string template, string name, string genus, string epithet, int? cost, IReadOnlyDictionary<string, double>? stats,
        SpeciesTemplateStats? templateStats, int startingSkin, StartingSkinSource source, string? startName)
    {
        var entry = AddSpecies(vanilla, id, template, name, genus, epithet, cost, stats, templateStats, startingSkin);
        try
        {
            AddStartingSkin(install, index, reader, vanilla, entry.Id, source, startName);
        }
        catch
        {
            RemoveSpeciesForRollback(entry);
            throw;
        }
        return entry;
    }

    /// <summary>
    /// Gives one of this mod's species its starting skin: a skins[] entry flagged "starting" with base "0" (the plain template
    /// clone), its PNGs in skins/&lt;id&gt;/. Copies take every map the source skin has.
    /// </summary>
    public SkinEntry AddStartingSkin(GameInstall install, AssetIndex index, IAssetReader reader, IReadOnlyList<SpeciesSkins> vanilla,
        string speciesId, StartingSkinSource source, string? name = null)
    {
        var species = Manifest.Species.FirstOrDefault(s => s.Id == speciesId)
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, "Species not found: " + speciesId);
        var key = species.Key(Manifest.Id);
        if (StartingSkins.Of(Manifest, key) is { } existing)
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"{speciesId} already has a starting skin ({existing.Id}).");
        var template = vanilla.FirstOrDefault(s => string.Equals(s.SpeciesId, species.Base, StringComparison.OrdinalIgnoreCase) && !s.Vivarium)
            ?? throw new TyrantException(TyrantErrorCode.DataMissing, $"'{species.Base}' is not in the game data dump; run the data dump first.");
        var templateSkin = template.Skins.FirstOrDefault(s => s.Index == species.StartingSkin) ?? template.Skins.FirstOrDefault()
            ?? throw new TyrantException(TyrantErrorCode.ModInvalid, $"{template.SpeciesId} has no skins.");

        var id = StartingSkinId(speciesId);
        int? newStart = null; // copying another template skin: the species' game object is copied from it too
        var entry = new SkinEntry { Id = id, Species = key, Base = "0", Starting = true };
        var folder = Path.Combine(Dir, "skins", id);
        try
        {
            switch (source)
            {
                case CopySkin copy:
                    var from = vanilla.FirstOrDefault(s => string.Equals(s.SpeciesId, copy.SpeciesId, StringComparison.OrdinalIgnoreCase));
                    if (from is null || from.Vivarium || from.SpeciesId.StartsWith("tyrant.", StringComparison.Ordinal))
                        throw new TyrantException(TyrantErrorCode.TargetNotFound, $"'{copy.SpeciesId}' must be a base-game animal (not a vivarium or custom species) from the data dump.");
                    var skin = from.Skins.FirstOrDefault(s => s.Index == copy.Skin)
                        ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"{from.SpeciesId} has no skin number {copy.Skin}. Its skins: {string.Join(", ", from.Skins.Select(s => $"{s.Index} {s.Name}"))}.");
                    var hasMale = skin.Male.ContainsKey("diffuse");
                    if (hasMale) entry.Male = Template(install, index, reader, id, "male", skin.Male, TemplateSlots, InfantTemplateSlots);
                    if (skin.Female.ContainsKey("diffuse")) entry.Female = Template(install, index, reader, id, "female", skin.Female, TemplateSlots, hasMale ? [] : InfantTemplateSlots);
                    if (entry.Male is null && entry.Female is null)
                        throw new TyrantException(TyrantErrorCode.TargetNotFound, $"{from.SpeciesId}'s skin {skin.Name} has no textures in the game data to copy.");
                    if (!string.Equals(from.SpeciesId, species.Base, StringComparison.OrdinalIgnoreCase))
                        entry.CopiedFrom = new SkinSource { Species = from.SpeciesId, Skin = skin.Index };
                    else if (skin.Index != species.StartingSkin) newStart = skin.Index;
                    entry.Name = string.IsNullOrWhiteSpace(name) ? skin.Name : name.Trim();
                    break;
                case OwnPngs own:
                    foreach (var png in new[] { own.Male, own.Female }.OfType<string>())
                    {
                        if (!File.Exists(png)) throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{png}' does not exist.");
                        if (!IsPng(png)) throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{png}' is not a PNG image (the game reads PNG only).");
                    }
                    var maps = TemplateSlots[1..];
                    var infantMaps = InfantTemplateSlots[1..]; // no infantDiffuse: babies wear the modder's diffuse
                    entry.Male = OwnDiffuse(id, "male", own.Male, Template(install, index, reader, id, "male", templateSkin.Male, maps, infantMaps, needsDiffuse: false));
                    entry.Female = OwnDiffuse(id, "female", own.Female ?? own.Male, Template(install, index, reader, id, "female", templateSkin.Female, maps, [], needsDiffuse: false));
                    entry.Name = string.IsNullOrWhiteSpace(name) ? species.Name : name.Trim();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(source));
            }
            var arranged = Arranged([entry, .. Manifest.Skins]); // listed first
            Manifest.Skins.Clear();
            Manifest.Skins.AddRange(arranged);
            Save();
            if (newStart is int start)
            {
                var old = species.StartingSkin;
                species.StartingSkin = start;
                try { SaveSpecies(); }
                catch { species.StartingSkin = old; throw; }
            }
        }
        catch
        {
            Manifest.Skins.Remove(entry);
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
        return entry;
    }

    /// <summary>
    /// Makes another skin of one of this mod's species its starting skin (skin 0). What it took from the old starting skin
    /// (maps it has no PNG for, colours, portraits, swatch) is copied into it first, so it looks as before; the old starting
    /// skin stays as an ordinary skin based on 0. Returns the new and the old starting skin (null when there was none).
    /// </summary>
    public (SkinEntry Made, SkinEntry? Previous) MakeStartingSkin(string skinId)
    {
        var skin = Skin(skinId);
        if (skin.Starting) throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{skin.Name}' is already its species' starting skin.");
        if (!Manifest.Species.Any(s => s.Key(Manifest.Id) == skin.Species))
            throw new TyrantException(TyrantErrorCode.ModInvalid,
                $"'{skin.Name}' is a skin of {skin.Species}: only a skin of one of this mod's own species can be its starting skin.");
        var previous = StartingSkins.Of(Manifest, skin.Species);
        var order = Manifest.Skins.ToList();
        var (male, female, colors, portraits, thumbnail, baseSkin) =
            (skin.Male, skin.Female, skin.Colors, skin.Portraits is null ? null : new Dictionary<string, string>(skin.Portraits), skin.Thumbnail, skin.Base);
        var copied = new List<string>();
        try
        {
            if (previous is not null && StartingSkins.InheritsFrom(Manifest, skin, out _))
            {
                skin.Male = TakeMaps(skin.Id, "male", previous.Male, skin.Male, copied);
                skin.Female = TakeMaps(skin.Id, "female", previous.Female, skin.Female, copied);
                if (skin.Colors is null && previous.Colors is not null) skin.Colors = SkinColors.Parse(Json.Parse(Json.Write(previous.Colors.ToJson())), skin.Id);
                if (previous.Portraits is { Count: > 0 })
                {
                    skin.Portraits ??= [];
                    foreach (var (slot, file) in previous.Portraits) skin.Portraits.TryAdd(slot, file);
                }
                skin.Thumbnail ??= previous.Thumbnail;
            }
            if (previous is not null) previous.Starting = false;
            skin.Starting = true;
            skin.Base = "0";
            var arranged = Arranged([skin, .. Manifest.Skins.Where(s => s != skin)]);
            Manifest.Skins.Clear();
            Manifest.Skins.AddRange(arranged);
            Save();
        }
        catch
        {
            (skin.Male, skin.Female, skin.Colors, skin.Portraits, skin.Thumbnail, skin.Base, skin.Starting) = (male, female, colors, portraits, thumbnail, baseSkin, false);
            if (previous is not null) previous.Starting = true;
            Manifest.Skins.Clear();
            Manifest.Skins.AddRange(order);
            foreach (var file in copied)
                try { File.Delete(Path.Combine(Dir, file.Replace('/', Path.DirectorySeparatorChar))); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
        return (Skin(skinId), previous is null ? null : Skin(previous.Id));
    }

    /// <summary>The skin's maps for one sex plus copies of the old starting skin's PNGs for the slots it has none for.</summary>
    private Dictionary<string, string>? TakeMaps(string skinId, string sex, IReadOnlyDictionary<string, string>? from, Dictionary<string, string>? own, List<string> copied)
    {
        if (from is null) return own;
        var maps = own is null ? new Dictionary<string, string>(StringComparer.Ordinal) : new Dictionary<string, string>(own, StringComparer.Ordinal);
        foreach (var (slot, file) in from)
        {
            if (maps.ContainsKey(slot) || Path.GetFullPath(Path.Combine(Dir, file)) is not { } source || !ModPaths.IsInside(source, Dir) || !File.Exists(source)) continue;
            var copy = $"skins/{skinId}/{SkinFileName(sex, slot)}";
            var path = Path.Combine(Dir, copy.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(source, path, overwrite: true);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            maps[slot] = copy;
            copied.Add(copy);
        }
        return maps.Count == 0 ? own : maps;
    }

    private Dictionary<string, string> OwnDiffuse(string skinId, string sex, string png, Dictionary<string, string> files)
    {
        var file = $"skins/{skinId}/{SkinFileName(sex, "diffuse")}";
        var path = Path.Combine(Dir, file.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(png, path, overwrite: true);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        files["diffuse"] = file;
        return files;
    }

    /// <summary>"&lt;species&gt;-start", else -start-2, -start-3…: unused by a skin and without a leftover skins/ folder.</summary>
    private string StartingSkinId(string speciesId)
    {
        for (var n = 1; ; n++)
        {
            var id = n == 1 ? $"{speciesId}-start" : $"{speciesId}-start-{n}";
            if (id.Length > 64) id = id[^64..].TrimStart('-');
            if (!Manifest.Skins.Any(s => s.Id == id) && !Directory.Exists(Path.Combine(Dir, "skins", id))) return id;
        }
    }
}
