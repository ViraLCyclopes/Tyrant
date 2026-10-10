namespace Tyrant.Core.Textures;

internal sealed record MipLevel(int Width, int Height, byte[] Rgba);

/// <summary>How a level is averaged: colour in linear light, masks as stored, packed normals as unit vectors.</summary>
internal enum MipFilter
{
    Srgb,
    Plain,
    PackedNormal,
}

/// <summary>Tyrant's own mipmaps and the size and row fixes before compression (RGBA, 4 bytes per pixel).</summary>
internal static class MipChain
{
    private static readonly float[] ToLinear = Enumerable.Range(0, 256).Select(v =>
    {
        var c = v / 255f;
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }).ToArray();

    private static byte ToSrgb(float linear)
    {
        var c = linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
        return (byte)Math.Clamp((int)MathF.Round(c * 255f), 0, 255);
    }

    private static byte Byte(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);

    /// <summary>Every level down to 1×1, largest first.</summary>
    public static List<MipLevel> Build(byte[] rgba, int width, int height, MipFilter filter)
    {
        var levels = new List<MipLevel> { new(width, height, rgba) };
        while (levels[^1].Width > 1 || levels[^1].Height > 1) levels.Add(Half(levels[^1], filter));
        return levels;
    }

    /// <summary>The next level: each pixel averages a 2×2 block (an odd last row or column is used once more).</summary>
    public static MipLevel Half(MipLevel level, MipFilter filter)
    {
        int w = level.Width, h = level.Height, w2 = Math.Max(1, w / 2), h2 = Math.Max(1, h / 2);
        var src = level.Rgba;
        var dst = new byte[w2 * h2 * 4];
        Span<int> at = stackalloc int[4];
        for (var y = 0; y < h2; y++)
        for (var x = 0; x < w2; x++)
        {
            int x0 = Math.Min(2 * x, w - 1), x1 = Math.Min(2 * x + 1, w - 1), y0 = Math.Min(2 * y, h - 1), y1 = Math.Min(2 * y + 1, h - 1);
            at[0] = (y0 * w + x0) * 4; at[1] = (y0 * w + x1) * 4; at[2] = (y1 * w + x0) * 4; at[3] = (y1 * w + x1) * 4;
            var o = (y * w2 + x) * 4;
            switch (filter)
            {
                case MipFilter.Srgb:
                    for (var c = 0; c < 3; c++)
                        dst[o + c] = ToSrgb((ToLinear[src[at[0] + c]] + ToLinear[src[at[1] + c]] + ToLinear[src[at[2] + c]] + ToLinear[src[at[3] + c]]) / 4f);
                    dst[o + 3] = Byte((src[at[0] + 3] + src[at[1] + 3] + src[at[2] + 3] + src[at[3] + 3]) / 4f);
                    break;
                case MipFilter.Plain:
                    for (var c = 0; c < 4; c++) dst[o + c] = Byte((src[at[0] + c] + src[at[1] + c] + src[at[2] + c] + src[at[3] + c]) / 4f);
                    break;
                default:
                    float nx = 0, ny = 0, nz = 0;
                    foreach (var i in at)
                    {
                        float px = src[i + 3] / 255f * 2 - 1, py = src[i + 1] / 255f * 2 - 1;
                        nx += px;
                        ny += py;
                        nz += MathF.Sqrt(MathF.Max(0, 1 - px * px - py * py));
                    }
                    var length = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (length < 1e-6f) (nx, ny, length) = (0, 0, 1);
                    var y8 = Byte((ny / length + 1) / 2 * 255);
                    (dst[o], dst[o + 1], dst[o + 2], dst[o + 3]) = (255, y8, y8, Byte((nx / length + 1) / 2 * 255));
                    break;
            }
        }
        return new MipLevel(w2, h2, dst);
    }

    /// <summary>Bilinear scale up to the next multiples of 4 (block compression needs them); unchanged when they already are.</summary>
    public static (byte[] Rgba, int Width, int Height) ScaleToMultipleOf4(byte[] rgba, int width, int height)
    {
        var (w4, h4) = TextureCooker.CookedSize(width, height);
        if (w4 == width && h4 == height) return (rgba, width, height);
        var dst = new byte[w4 * h4 * 4];
        for (var y = 0; y < h4; y++)
        {
            var sy = Math.Clamp((y + 0.5f) * height / h4 - 0.5f, 0, height - 1);
            int y0 = (int)sy, y1 = Math.Min(y0 + 1, height - 1);
            var fy = sy - y0;
            for (var x = 0; x < w4; x++)
            {
                var sx = Math.Clamp((x + 0.5f) * width / w4 - 0.5f, 0, width - 1);
                int x0 = (int)sx, x1 = Math.Min(x0 + 1, width - 1);
                var fx = sx - x0;
                for (var c = 0; c < 4; c++)
                {
                    float top = rgba[(y0 * width + x0) * 4 + c] * (1 - fx) + rgba[(y0 * width + x1) * 4 + c] * fx;
                    float bottom = rgba[(y1 * width + x0) * 4 + c] * (1 - fx) + rgba[(y1 * width + x1) * 4 + c] * fx;
                    dst[(y * w4 + x) * 4 + c] = Byte(top * (1 - fy) + bottom * fy);
                }
            }
        }
        return (dst, w4, h4);
    }

    /// <summary>PNG rows run top first; Unity's raw texture data runs bottom first.</summary>
    public static void FlipRows(byte[] rgba, int width, int height)
    {
        var row = width * 4;
        var temp = new byte[row];
        for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
        {
            Buffer.BlockCopy(rgba, top * row, temp, 0, row);
            Buffer.BlockCopy(rgba, bottom * row, rgba, top * row, row);
            Buffer.BlockCopy(temp, 0, rgba, bottom * row, row);
        }
    }
}
