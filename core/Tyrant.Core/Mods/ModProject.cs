using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

public sealed record SkinTemplateOptions(bool Male, bool Female, bool Maps);

/// <summary>A mod being made in the workspace: &lt;workspace&gt;/mods/&lt;id&gt;/ with mod.json and textures/.</summary>
public sealed class ModProject
{
    public const string TexturesFolder = "textures";

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private ModProject(string dir, ModManifest manifest)
    {
        Dir = dir;
        Manifest = manifest;
    }

    public string Dir { get; }
    public ModManifest Manifest { get; }
    public string Id => Manifest.Id;

    public static string RootOf(Workspace ws) => Path.Combine(ws.Dir, "mods");

    /// <summary>Folder names under mods/ that hold a mod.json, sorted.</summary>
    public static IReadOnlyList<string> Ids(Workspace ws) =>
        Directory.Exists(RootOf(ws))
            ? Directory.GetDirectories(RootOf(ws)).Where(d => File.Exists(Path.Combine(d, ModManifest.FileName))).Select(Path.GetFileName)
                .OfType<string>().Order(StringComparer.Ordinal).ToList()
            : [];

    public static ModProject Create(Workspace ws, string id, string? name, string? author)
    {
        if (!ModId.IsValid(id))
            throw new TyrantException(TyrantErrorCode.ModIdInvalid, $"'{id}' is not a valid mod id: use 3–64 lowercase letters, digits or '-', e.g. red-spot-carcharo.");
        var dir = Path.Combine(RootOf(ws), id);
        if (Directory.Exists(dir)) throw new TyrantException(TyrantErrorCode.ModIdInvalid, $"A mod called '{id}' already exists in this workspace; choose another id.");
        var project = new ModProject(dir, new ModManifest { Id = id, Name = string.IsNullOrWhiteSpace(name) ? id : name.Trim(), Author = author });
        project.Save();
        return project;
    }

    public static ModProject Open(Workspace ws, string id)
    {
        var dir = Path.Combine(RootOf(ws), id);
        var path = Path.Combine(dir, ModManifest.FileName);
        if (!ModId.IsValid(id) || !File.Exists(path))
            throw new TyrantException(TyrantErrorCode.ModNotFound, $"There is no mod '{id}' in this workspace ({RootOf(ws)}).");
        try
        {
            var manifest = ModManifest.Parse(File.ReadAllText(path));
            if (manifest.Id != id) throw new ManifestException($"its id \"{manifest.Id}\" does not match its folder \"{id}\"");
            return new ModProject(dir, manifest);
        }
        catch (ManifestException ex)
        {
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"Mod '{id}' cannot be read: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds (or updates) a replacement of one game texture, found by name, Addressables path, GUID or ref. The PNG is copied
    /// into textures/; without one, the texture's exported PNG (assets/textures/…) is used.
    /// </summary>
    public TextureReplacement Replace(Workspace ws, AssetIndex index, string texture, string? png)
    {
        var target = FindTexture(index, texture);
        var source = png ?? TextureExporter.OutputPathFor(target, ws.AssetsDir);
        if (!File.Exists(source))
            throw new TyrantException(TyrantErrorCode.ModInvalid, png is null
                ? $"No PNG was given and '{target.Name}' has not been exported yet ({source}). Export it from the Assets tab, edit it, or pick a PNG."
                : $"'{source}' does not exist.");
        if (!IsPng(source)) throw new TyrantException(TyrantErrorCode.ModInvalid, $"'{source}' is not a PNG image.");

        var file = $"{TexturesFolder}/{TextureExporter.Sanitize(target.Name)}.png";
        Directory.CreateDirectory(Path.Combine(Dir, TexturesFolder));
        var destination = Path.Combine(Dir, file.Replace('/', Path.DirectorySeparatorChar));
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            File.Copy(source, destination, overwrite: true);

        Manifest.Replace.RemoveAll(r => string.Equals(r.Texture, target.Name, StringComparison.OrdinalIgnoreCase));
        var entry = new TextureReplacement { Texture = target.Name, Key = target.ContainerPath, Guid = target.Guid, File = file };
        Manifest.Replace.Add(entry);
        Save();
        return entry;
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, ModManifest.FileName), Manifest.ToJson());
    }

    private static AssetRecord FindTexture(AssetIndex index, string key)
    {
        try
        {
            return index.Resolve(key, "Texture2D");
        }
        catch (TyrantException ex) when (ex.Code == TyrantErrorCode.AssetNotFound)
        {
            var byName = index.Assets.Where(a => a.Type == "Texture2D" && string.Equals(a.Name, key, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
            return byName.Count switch
            {
                1 => byName[0],
                0 => throw new TyrantException(TyrantErrorCode.TargetNotFound,
                    $"No texture called '{key}' is in the asset index. Copy its name, Addressables path or GUID from the Assets tab."),
                _ => throw new TyrantException(TyrantErrorCode.AssetAmbiguous, $"Several textures are called '{key}'; use its Addressables path or GUID instead."),
            };
        }
    }

    private static bool IsPng(string path)
    {
        using var stream = File.OpenRead(path);
        var head = new byte[PngSignature.Length];
        return stream.Read(head, 0, head.Length) == head.Length && head.AsSpan().SequenceEqual(PngSignature);
    }

    private static readonly (string Slot, string Suffix)[] TemplateSlots = [("diffuse", "D"), ("normal", "N"), ("extra", "extra"), ("pattern", "pattern")];

    /// <summary>
    /// Adds a new skin based on a vanilla skin: exports the base textures (diffuse; with Maps also normal, extra and pattern) into
    /// skins/&lt;skin id&gt;/ as an editable template and records the skin in mod.json.
    /// </summary>
    public SkinEntry AddSkin(Workspace ws, GameInstall install, AssetIndex index, IAssetReader reader, IReadOnlyList<SpeciesSkins> species,
        string speciesId, string name, string? baseSkin, SkinTemplateOptions options)
    {
        if (!options.Male && !options.Female) throw new TyrantException(TyrantErrorCode.ModInvalid, "Choose male, female or both for the new skin.");
        var target = species.FirstOrDefault(s => string.Equals(s.SpeciesId, speciesId, StringComparison.OrdinalIgnoreCase))
            ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, $"No species '{speciesId}' in the game data. Pick one from the list (Species tab).");
        if (target.Skins.Count == 0) throw new TyrantException(TyrantErrorCode.TargetNotFound, $"{target.SpeciesId} has no skins in the game data to start from.");
        var based = baseSkin is null ? target.Skins[0]
            : int.TryParse(baseSkin, out var number) ? target.Skins.FirstOrDefault(s => s.Index == number)
            : target.Skins.FirstOrDefault(s => string.Equals(s.Name, baseSkin, StringComparison.OrdinalIgnoreCase));
        if (based is null)
            throw new TyrantException(TyrantErrorCode.TargetNotFound, $"{target.SpeciesId} has no skin '{baseSkin}'. Its skins: {string.Join(", ", target.Skins.Select(s => s.Name))}.");

        var id = SkinId(name);
        // By name when that is unambiguous (readable in mod.json); by number when the name repeats or the game left it empty.
        var named = based.Name != $"Skin {based.Index}" && target.Skins.Count(s => string.Equals(s.Name, based.Name, StringComparison.OrdinalIgnoreCase)) == 1;
        var entry = new SkinEntry { Id = id, Species = target.SpeciesId, Name = name.Trim(), Base = named ? based.Name : based.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var slots = options.Maps ? TemplateSlots : TemplateSlots[..1];
        var folder = Path.Combine(Dir, "skins", id);
        var existed = Directory.Exists(folder);
        var before = existed ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        try
        {
            if (options.Male) entry.Male = Template(ws, install, index, reader, id, "male", based.Male, slots);
            if (options.Female) entry.Female = Template(ws, install, index, reader, id, "female", based.Female, slots);
        }
        catch
        {
            try // remove the template files written before the failure; files that were already there stay
            {
                if (!existed) { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
                else
                    foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                        if (!before.Contains(file)) File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
        Manifest.Skins.Add(entry);
        Save();
        return entry;
    }

    private Dictionary<string, string> Template(Workspace ws, GameInstall install, AssetIndex index, IAssetReader reader, string skinId, string sex,
        IReadOnlyDictionary<string, string> textures, (string Slot, string Suffix)[] slots)
    {
        if (!textures.ContainsKey("diffuse"))
            throw new TyrantException(TyrantErrorCode.TargetNotFound, $"The base skin has no {sex} diffuse texture in the game data; pick another base or leave {sex} out.");
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (slot, suffix) in slots)
        {
            if (!textures.TryGetValue(slot, out var guid)) continue;
            var file = $"skins/{skinId}/{sex}_{suffix}.png";
            reader.WriteTexture(install, index.Resolve(guid, "Texture2D"), Path.Combine(Dir, file.Replace('/', Path.DirectorySeparatorChar)));
            files[slot] = file;
        }
        return files;
    }

    /// <summary>A skin id from its name ("Red spot" → "red-spot"), unique within the mod.</summary>
    private string SkinId(string name)
    {
        var slug = new string(name.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        if (slug.Length < 3) slug = (slug + "-skin").Trim('-');
        if (slug.Length > 60) slug = slug[..60].TrimEnd('-');
        var id = slug;
        for (var n = 2; Manifest.Skins.Any(s => s.Id == id); n++) id = $"{slug}-{n}";
        return id;
    }
}
