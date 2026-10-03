namespace Tyrant.Core.Install;

/// <summary>
/// Whether a path lies inside a folder (the game folder), robust to a folder at a drive root, the \?\ prefix, case, and
/// junctions or symbolic links on the way (a workspace reached through a link into the game folder is inside it).
/// </summary>
public static class PathGuard
{
    public static bool IsInside(string path, string folder)
    {
        var root = WithSeparator(Normalize(folder));
        var candidate = WithSeparator(Normalize(path));
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A full path without the \?\ prefix, with every existing link on the way resolved.</summary>
    public static string Normalize(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) path = @"\\" + path[8..];
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path[4..];
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? "";
        var resolved = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            resolved = Path.Combine(resolved, part);
            try
            {
                if (Directory.Exists(resolved) && Directory.ResolveLinkTarget(resolved, returnFinalTarget: true) is { } target)
                    resolved = Path.GetFullPath(target.FullName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // an unreadable link stays as written
            }
        }
        return resolved;
    }

    private static string WithSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
