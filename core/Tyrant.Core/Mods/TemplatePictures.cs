using System.Text.Json;
using Tyrant.Core.Assets;
using Tyrant.Core.Data;
using Tyrant.Core.Errors;
using Tyrant.Core.Install;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Mods;

/// <summary>Nursery: AnimalData.heroRender (the Nursery card); Paleopedia: the background of AnimalData.infoSign (the species' page).</summary>
public enum TemplatePictureKind { Nursery, Paleopedia }

/// <summary>A game species' picture saved as a PNG: the species as the dump names it, the texture written and its size.</summary>
public sealed record TemplatePicture(string Species, TemplatePictureKind Kind, string Texture, int Width, int Height, string Path);

/// <summary>
/// Saves a game species' own Nursery or Paleopedia picture as a PNG, for a modder to design a custom species' picture over.
/// These are the game's files: they are written only where the user asks, never into a mod or the game folder.
/// </summary>
public static class TemplatePictures
{
    private const string AnimalType = "PrehistoricKingdom.AnimalData";

    public static string Label(TemplatePictureKind kind) => kind == TemplatePictureKind.Nursery ? "Nursery picture" : "Paleopedia picture";

    /// <param name="index">The workspace's asset index when the caller has it loaded; read from the workspace otherwise.</param>
    public static TemplatePicture Save(Workspace ws, GameInstall install, IAssetReader reader, string speciesId, TemplatePictureKind kind, string pngPath,
        AssetIndex? index = null)
    {
        var label = Label(kind);
        if (speciesId.StartsWith("tyrant.", StringComparison.Ordinal))
            throw new TyrantException(TyrantErrorCode.TargetNotFound,
                $"'{speciesId}' is a custom species: it shows its template's {label}. Save the picture of its template (the game species it is based on).");
        var path = Path.GetFullPath(Path.GetExtension(pngPath).Length == 0 ? pngPath + ".png" : pngPath);
        if (install.ContainsPath(path))
            throw new TyrantException(TyrantErrorCode.OutputInGameFolder, $"Refusing to write '{path}' inside the game folder.");
        if (PathGuard.IsInside(path, ModProject.RootOf(ws)))
            throw new TyrantException(TyrantErrorCode.ModInvalid,
                $"Save the {label} outside the mods folder: it is the game's picture, for designing yours over, not to be shared or put in a mod.");

        var (species, name) = Reference(ws, speciesId, kind);
        index ??= AssetIndex.Load(AssetIndex.PathIn(ws));
        AssetRecord? texture;
        using (reader.Batch(install))
        {
            var walk = new Walk(reader, install, index);
            texture = kind == TemplatePictureKind.Nursery ? walk.HeroTexture(name) : walk.InfoSignTexture(name);
        }
        if (texture is null)
            throw new TyrantException(TyrantErrorCode.AssetNotFound,
                $"Could not find {species}'s {label} in the asset index (it follows '{name}' to its texture). If the game was updated, index the assets again (Workspace → Index assets, or 'tyrant assets index').",
                FixAction.ReindexAssets);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var facts = reader.WriteTexture(install, texture, path);
        return new TemplatePicture(species, kind, texture.Name, facts.Width, facts.Height, path);
    }

    /// <summary>The species' id as the dump writes it, and the name of the object its picture field references.</summary>
    private static (string Species, string Name) Reference(Workspace ws, string speciesId, TemplatePictureKind kind)
    {
        var store = DataStore.Open(ws);
        DataType type;
        try
        {
            type = store.FindType(AnimalType);
        }
        catch (TyrantException ex) when (ex.Code is TyrantErrorCode.AssetNotFound or TyrantErrorCode.AssetAmbiguous)
        {
            throw new TyrantException(TyrantErrorCode.DataMissing,
                "The data dump has no animals. Run the data dump again (Workspace → Run data dump, or 'tyrant dump run').", FixAction.RefreshWorkspace, ex);
        }
        var field = kind == TemplatePictureKind.Nursery ? "heroRender" : "infoSign";
        foreach (var (_, root) in store.LoadAll(type))
        {
            if (!root.TryGetProperty("speciesID", out var id) || id.ValueKind != JsonValueKind.String
                || !string.Equals(id.GetString(), speciesId, StringComparison.OrdinalIgnoreCase)) continue;
            var species = id.GetString()!;
            if (root.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Object && value.TryGetProperty("$ref", out var reference)
                && reference.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } text)
                return (species, text);
            throw new TyrantException(TyrantErrorCode.AssetNotFound, $"The game data gives {species} no {Label(kind)} ({field} is empty).");
        }
        throw new TyrantException(TyrantErrorCode.TargetNotFound,
            $"No species '{speciesId}' in the game data dump; check the name on the Species tab (or 'tyrant species list'), or run the data dump again.");
    }

    /// <summary>Follows references between indexed objects (as the asset browser lists them) through the reader.</summary>
    private sealed class Walk(IAssetReader reader, GameInstall install, AssetIndex index)
    {
        /// <summary>The Nursery picture: the Sprite's texture (m_RD.texture), else the Texture2D of the sprite's name.</summary>
        public AssetRecord? HeroTexture(string sprite)
        {
            foreach (var record in Named("Sprite", sprite))
                if (SpriteTexture(record) is { } texture) return texture;
            return Named("Texture2D", sprite).FirstOrDefault();
        }

        /// <summary>
        /// The Paleopedia picture: the info sign canvas's child BackgroundImg (else its first child), whose uGUI Image shows a
        /// Sprite of the picture. Followed by reference: the textures are not always named like their species.
        /// </summary>
        public AssetRecord? InfoSignTexture(string canvas)
        {
            foreach (var root in Named("GameObject", canvas))
            {
                var transform = Refs(root, IsComponent).FirstOrDefault(r => r.Type is "Transform" or "RectTransform");
                if (transform is null) continue;
                var children = Refs(transform, f => f.StartsWith("m_Children[", StringComparison.Ordinal))
                    .Select(child => Refs(child, f => f == "m_GameObject").FirstOrDefault(r => r.Type == "GameObject"))
                    .OfType<AssetRecord>()
                    .ToList();
                var background = children.Where(c => c.Name == "BackgroundImg").ToList();
                foreach (var child in background.Count > 0 ? background : children.Take(1))
                {
                    var images = Refs(child, IsComponent).Where(r => r.Type == "MonoBehaviour").OrderBy(r => r.Script == "Image" ? 0 : 1);
                    foreach (var image in images)
                        if (Refs(image, f => f == "m_Sprite").FirstOrDefault(r => r.Type == "Sprite") is { } sprite && SpriteTexture(sprite) is { } texture)
                            return texture;
                }
            }
            return null;
        }

        private static bool IsComponent(string field) => field.StartsWith("m_Component[", StringComparison.Ordinal);

        private AssetRecord? SpriteTexture(AssetRecord sprite) =>
            Refs(sprite, f => f == "m_RD.texture").FirstOrDefault(r => r.Type == "Texture2D");

        private IEnumerable<AssetRecord> Named(string type, string name) =>
            index.Assets.Where(a => a.Type == type && string.Equals(a.Name, name, StringComparison.Ordinal))
                .OrderBy(a => a.IsBuiltIn ? 0 : 1);

        /// <summary>The indexed objects the owner's matching fields reference, in field order (unknown targets skipped).</summary>
        private List<AssetRecord> Refs(AssetRecord owner, Func<string, bool> field)
        {
            var inspection = reader.Inspect(install, owner);
            var found = new List<AssetRecord>();
            foreach (var reference in inspection.References.Where(r => field(r.Field)))
            {
                var bundle = reference.FileId == 0 ? owner.Bundle
                    : reference.FileId - 1 < inspection.ExternalFiles.Count ? index.BundleOfArchive(inspection.ExternalFiles[reference.FileId - 1]) : null;
                if (bundle is not null && index.Find(bundle, reference.PathId) is { } target) found.Add(target);
            }
            return found;
        }
    }
}
