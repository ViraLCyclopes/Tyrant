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

    /// <summary>
    /// True for Unity's packed form, judged from up to ~4096 pixels: red filled, green and blue equal (both Y), and
    /// alpha varying around the middle (X). A red skin or wound texture fails this: its alpha is solid or its green and
    /// blue differ. Names are no guide — the detail normal map is called "Detail_Skin".
    /// </summary>
    public static bool LooksPacked(ReadOnlySpan<byte> rgba)
    {
        var pixels = rgba.Length / 4;
        if (pixels == 0) return false;
        var step = Math.Max(1, pixels / 4096);
        double red = 0, greenBlueGap = 0, alpha = 0, alphaSquares = 0;
        var samples = 0;
        for (var p = 0; p < pixels; p += step)
        {
            var i = p * 4;
            red += rgba[i];
            greenBlueGap += Math.Abs(rgba[i + 1] - rgba[i + 2]);
            alpha += rgba[i + 3];
            alphaSquares += rgba[i + 3] * (double)rgba[i + 3];
            samples++;
        }
        var alphaMean = alpha / samples;
        var alphaSpread = Math.Sqrt(Math.Max(0, alphaSquares / samples - alphaMean * alphaMean));
        return red / samples >= 240 && greenBlueGap / samples <= 12 && alphaMean is >= 64 and <= 192 && alphaSpread >= 8;
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
