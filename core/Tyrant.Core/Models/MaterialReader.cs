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
        var shader = material["m_Shader"];
        var shaderFile = shader.IsDummy ? 0 : shader["m_FileID"].AsInt;
        var shaderPath = shader.IsDummy ? 0 : shader["m_PathID"].AsLong;
        var floats = new Dictionary<string, float>(StringComparer.Ordinal);
        var floatArray = material["m_SavedProperties.m_Floats.Array"];
        if (!floatArray.IsDummy)
            foreach (var pair in floatArray.Children) floats[pair["first"].AsString] = pair["second"].AsFloat;
        var name = material["m_Name"];
        return new MaterialModel(name.IsDummy ? "" : name.AsString, slots)
        {
            ShaderArchive = shaderFile > 0 && shaderFile <= externals.Count ? externals[shaderFile - 1] : null,
            ShaderPathId = shaderFile >= 0 && shaderFile <= externals.Count ? shaderPath : 0,
            Keywords = Keywords(material),
            Floats = floats,
        };
    }

    /// <summary>Unity 2022 keeps keywords as a list; older materials as one space-separated string.</summary>
    private static List<string> Keywords(AssetTypeValueField material)
    {
        var valid = material["m_ValidKeywords.Array"];
        if (!valid.IsDummy) return valid.Children.Select(k => k.AsString).ToList();
        var joined = material["m_ShaderKeywords"];
        return joined.IsDummy ? [] : joined.AsString.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
