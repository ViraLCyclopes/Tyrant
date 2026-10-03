using AssetsTools.NET;

namespace Tyrant.Core.Models;

/// <summary>Reads a Unity Material's texture slots (m_SavedProperties.m_TexEnvs).</summary>
public static class MaterialReader
{
    /// <param name="externals">The material's file's external files (Metadata.Externals' PathName), indexed by m_FileID - 1.</param>
    public static MaterialModel Read(AssetTypeValueField material, IReadOnlyList<string> externals)
    {
        var slots = new List<TextureSlot>();
        var envs = material["m_SavedProperties.m_TexEnvs.Array"];
        if (!envs.IsDummy)
        {
            foreach (var pair in envs.Children)
            {
                var texture = pair["second.m_Texture"];
                var pathId = texture["m_PathID"].AsLong;
                var fileId = texture["m_FileID"].AsInt;
                if (pathId == 0 || fileId < 0 || fileId > externals.Count) continue; // empty slot, or a file the material does not list
                slots.Add(new TextureSlot(pair["first"].AsString, fileId == 0 ? null : externals[fileId - 1], pathId));
            }
        }
        var name = material["m_Name"];
        return new MaterialModel(name.IsDummy ? "" : name.AsString, slots);
    }
}
