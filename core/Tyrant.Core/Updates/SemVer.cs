namespace Tyrant.Core.Updates;

/// <summary>Version strings as releases write them: "v0.2.0", "0.2.0", "0.2.0-beta" (a pre-release sorts before its release).</summary>
public static class SemVer
{
    public static string Normalize(string version) => version.Trim().TrimStart('v', 'V');

    public static int Compare(string a, string b)
    {
        var (x, y) = (Parse(a), Parse(b));
        if (x is null || y is null) return x is null ? (y is null ? 0 : -1) : 1;
        for (var i = 0; i < 3; i++)
            if (x.Value.Numbers[i] != y.Value.Numbers[i]) return x.Value.Numbers[i].CompareTo(y.Value.Numbers[i]);
        if (x.Value.Pre == y.Value.Pre) return 0;
        if (x.Value.Pre is null) return 1;
        if (y.Value.Pre is null) return -1;
        return string.CompareOrdinal(x.Value.Pre, y.Value.Pre);
    }

    private static (int[] Numbers, string? Pre)? Parse(string version)
    {
        var text = Normalize(version);
        var dash = text.IndexOf('-');
        var core = dash < 0 ? text : text[..dash];
        var parts = core.Split('.');
        if (parts.Length != 3 || !parts.All(p => int.TryParse(p, out _))) return null;
        return (parts.Select(int.Parse).ToArray(), dash < 0 ? null : text[(dash + 1)..]);
    }
}
