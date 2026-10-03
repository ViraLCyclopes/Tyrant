using System.Globalization;
using System.Text.RegularExpressions;
using Tyrant.Core.Assets;

namespace Tyrant.Rpc.Assets;

public static partial class PreviewParts
{
    [GeneratedRegex(@"_LOD(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex LodSuffix();

    /// <summary>
    /// What a 3D preview shows: the most detailed LOD (Acro_LOD00 of LOD00..LOD02) plus parts without an LOD suffix.
    /// All LODs drawn together overlap and flicker; exports still include every LOD.
    /// </summary>
    public static IReadOnlyList<ModelPart> Pick(IReadOnlyList<ModelPart> parts)
    {
        var levels = parts.Select(p => (Part: p, Match: LodSuffix().Match(p.Name))).ToList();
        if (!levels.Any(l => l.Match.Success)) return parts;
        var best = levels.Where(l => l.Match.Success).Min(l => int.Parse(l.Match.Groups[1].Value, CultureInfo.InvariantCulture));
        return levels
            .Where(l => !l.Match.Success || int.Parse(l.Match.Groups[1].Value, CultureInfo.InvariantCulture) == best)
            .Select(l => l.Part)
            .ToList();
    }
}
