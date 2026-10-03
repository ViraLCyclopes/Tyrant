using AssetsTools.NET;

namespace Tyrant.Core.Assets;

public static class AssetSizes
{
    /// <summary>
    /// Bytes an object keeps outside itself: Texture2D/Mesh pixel or vertex data in a .resS stream (m_StreamData),
    /// AudioClip samples in a .resource file (m_Resource). The object's own size is only its header then.
    /// </summary>
    public static long StreamedBytes(AssetTypeValueField root)
    {
        var stream = root["m_StreamData"];
        if (!stream.IsDummy && !stream["size"].IsDummy) return stream["size"].AsUInt;
        var resource = root["m_Resource"];
        if (!resource.IsDummy && !resource["m_Size"].IsDummy) return (long)resource["m_Size"].AsULong;
        return 0;
    }
}
