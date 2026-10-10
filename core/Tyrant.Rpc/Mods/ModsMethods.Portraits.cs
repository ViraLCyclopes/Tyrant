using Tyrant.Core.Errors;
using Tyrant.Core.Mods;
using Tyrant.Rpc.Protocol;

namespace Tyrant.Rpc.Mods;

public sealed partial class ModsMethods
{
    [RpcMethod("mods.setPortraits")]
    public ModDetail SetPortraits(ModSetPortraitsParams p) =>
        Edit(p.Id, p.Revision, mod => mod.SetPortraits(p.Skin, Sources(p.Set)), $"set portraits of '{p.Skin}'");

    [RpcMethod("mods.setSpeciesPortraits")]
    public ModDetail SetSpeciesPortraits(ModSetSpeciesPortraitsParams p) =>
        Edit(p.Id, p.Revision, mod => mod.SetSpeciesPortraits(p.SpeciesId, Sources(p.Set)), "updated species portraits " + p.SpeciesId);

    /// <summary>The 12 pictures the game will show for a skin (or a custom species' starting skin), as files the app may load.</summary>
    [RpcMethod("mods.portraits")]
    public ModPortraitsResult Portraits(ModPortraitsParams p)
    {
        var (ws, install) = session.Current();
        var mod = ModProject.Open(ws, p.Id);
        var species = TrySpeciesFor(ws, install, mod);
        IReadOnlyList<ShownPortrait> rows;
        if (p.Skin is not null)
        {
            var skin = mod.Skin(p.Skin);
            rows = ShownPortraits.ForSkin(mod, skin, species is null ? null : BaseSkin(species, skin));
        }
        else
        {
            if (p.SpeciesId is null) throw new TyrantException(TyrantErrorCode.ModInvalid, "Give a skin or a species.");
            if (p.SpeciesId is null) throw new TyrantException(TyrantErrorCode.ModInvalid, "Give a skin or a species.");
            var entry = mod.Manifest.Species.FirstOrDefault(s => s.Id == p.SpeciesId)
                ?? throw new TyrantException(TyrantErrorCode.TargetNotFound, "Species not found: " + p.SpeciesId);
            var start = species?.FirstOrDefault(s => s.SpeciesId == entry.Key(mod.Id))?.Skins.FirstOrDefault();
            rows = ShownPortraits.ForSpecies(mod, entry, start);
        }
        var index = TryIndex(ws);
        return new ModPortraitsResult(rows.Select(r =>
        {
            var file = r.Source switch
            {
                null => null,
                var s when s.StartsWith("file:", StringComparison.Ordinal) => OwnMapCopy(ws, mod, Path.GetRelativePath(mod.Dir, s[5..])),
                var s when index is not null => SkinMaps.Write(s, install, index, Options.AssetReader, Path.Combine(PreviewsDir(ws, "portraits"), $"{s[5..]}.png")),
                _ => null,
            };
            var (width, height) = file is null ? (0, 0) : PngSize(file);
            return new ModPortraitDto(r.Slot, file, r.Own, r.Rendered, width, height);
        }).ToList());
    }

    private static Dictionary<string, PortraitSource?> Sources(IReadOnlyDictionary<string, ModPortraitSourceDto?> set) =>
        set.ToDictionary(pair => pair.Key, pair => pair.Value is null ? null
            : new PortraitSource(pair.Value.Png, pair.Value.Data is null ? null : DecodeData(pair.Key, pair.Value.Data), pair.Value.Rendered), StringComparer.Ordinal);

    private static byte[] DecodeData(string slot, string data)
    {
        try { return Convert.FromBase64String(data); }
        catch (FormatException) { throw new TyrantException(TyrantErrorCode.ModInvalid, $"Portrait '{slot}': the picture data is not base64."); }
    }

    /// <summary>A PNG's size from its header (IHDR); (0, 0) when it cannot be read.</summary>
    private static (int Width, int Height) PngSize(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            var header = new byte[24];
            if (stream.Read(header, 0, 24) != 24) return (0, 0);
            return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16)), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (0, 0); }
    }
}
