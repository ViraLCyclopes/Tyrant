using Tyrant.Core.Assets;
using Tyrant.Core.Errors;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

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
}
