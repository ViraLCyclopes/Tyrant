using StbImageSharp;
using StbImageWriteSharp;

namespace Tyrant.Core.Models;

/// <summary>
/// The animal extra map (R = smoothness, G = ambient occlusion) as glTF's occlusion/roughness/metallic texture
/// (R = occlusion, G = roughness = 1 − smoothness, B = metallic = 0), so Blender shades an export like the game.
/// </summary>
public static class OcclusionRoughness
{
    public static byte[] FromExtra(byte[] extraRgba)
    {
        var orm = new byte[extraRgba.Length];
        for (var o = 0; o + 3 < extraRgba.Length; o += 4)
        {
            orm[o] = extraRgba[o + 1];
            orm[o + 1] = (byte)(255 - extraRgba[o]);
            orm[o + 2] = 0;
            orm[o + 3] = 255;
        }
        return orm;
    }

    /// <summary>Writes &lt;extra&gt;_ORM.png next to the extra map; null when the extra map cannot be read.</summary>
    public static string? Write(string extraPng)
    {
        ImageResult image;
        try
        {
            image = ImageResult.FromMemory(File.ReadAllBytes(extraPng), StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
        var path = Path.Combine(Path.GetDirectoryName(extraPng)!, Path.GetFileNameWithoutExtension(extraPng) + "_ORM.png");
        using var stream = File.Create(path);
        new ImageWriter().WritePng(FromExtra(image.Data), image.Width, image.Height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        return path;
    }
}
