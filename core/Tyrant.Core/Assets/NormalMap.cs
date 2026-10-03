using System.Text.RegularExpressions;

namespace Tyrant.Core.Assets;

/// <summary>
/// Unity stores normal maps in a packed form (DXT5nm): X in alpha, Y in green, red filled, Z dropped (the shader
/// rebuilds it). Exported as-is they look red; modders expect the standard blue-purple tangent-space map.
/// </summary>
public static partial class NormalMap
{
    [GeneratedRegex(@"(_n|_nrm|_normal)$", RegexOptions.IgnoreCase)]
    private static partial Regex NormalSuffix();

    public static bool IsCandidate(string name) => NormalSuffix().IsMatch(name.Trim());

    /// <summary>True when the red channel is saturated, the tell of the packed form (checked on up to ~4096 pixels).</summary>
    public static bool LooksPacked(ReadOnlySpan<byte> rgba)
    {
        var pixels = rgba.Length / 4;
        if (pixels == 0) return false;
        var step = Math.Max(1, pixels / 4096);
        long red = 0;
        var samples = 0;
        for (var p = 0; p < pixels; p += step)
        {
            red += rgba[p * 4];
            samples++;
        }
        return (double)red / samples >= 240;
    }

    /// <summary>Rebuilds a standard normal map in place: R = X (from alpha), G = Y, B = Z = sqrt(1 - x² - y²), opaque.</summary>
    public static void Unpack(Span<byte> rgba)
    {
        for (var i = 0; i + 3 < rgba.Length; i += 4)
        {
            var x = rgba[i + 3] / 255.0 * 2 - 1;
            var y = rgba[i + 1] / 255.0 * 2 - 1;
            var z = Math.Sqrt(Math.Max(0, 1 - x * x - y * y));
            rgba[i] = rgba[i + 3];
            rgba[i + 2] = (byte)Math.Round((z * 0.5 + 0.5) * 255);
            rgba[i + 3] = 255;
        }
    }
}
