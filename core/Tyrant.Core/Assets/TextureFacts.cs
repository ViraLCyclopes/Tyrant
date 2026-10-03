using AssetsTools.NET;
using AssetsTools.NET.Texture;

namespace Tyrant.Core.Assets;

public sealed record TextureFacts(int Width, int Height, string Format, int MipCount, bool RebuiltNormal = false)
{
    /// <summary>Reads size, pixel format and mip count from a Texture2D's fields (no pixel data is decoded).</summary>
    public static TextureFacts Read(AssetTypeValueField texture)
    {
        var mips = texture["m_MipCount"];
        return new TextureFacts(
            texture["m_Width"].AsInt,
            texture["m_Height"].AsInt,
            ((TextureFormat)texture["m_TextureFormat"].AsInt).ToString(),
            mips.IsDummy ? 1 : Math.Max(1, mips.AsInt));
    }
}
