namespace Tyrant.Framework.Tests;

/// <summary>Test images shared with Tyrant.Core.Tests (linked there).</summary>
public static class NormalMapsFixture
{
    /// <summary>
    /// Unity's packed form (red filled, green = blue, X in alpha) whose left column is flat (alpha 128), as an atlas's empty
    /// edge is, while the other columns vary around the middle.
    /// </summary>
    public static byte[] PackedWithFlatLeftColumn(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                rgba[i] = 255;
                rgba[i + 1] = rgba[i + 2] = 128;
                rgba[i + 3] = x == 0 ? (byte)128 : (byte)(64 + (x * 37 + y * 11) % 128);
            }
        return rgba;
    }
}
