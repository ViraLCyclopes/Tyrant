using AssetsTools.NET;

namespace Tyrant.Core.Assets;

/// <summary>One reference (PPtr) from an object's fields: FileId 0 = same file, N = the file's N-th external.</summary>
public sealed record AssetReference(string Field, int FileId, long PathId);

public static class AssetReferences
{
    /// <summary>How many references the app shows; an object with more says the list was cut.</summary>
    public const int MaxShown = 500;

    /// <summary>Every non-null PPtr (an m_FileID/m_PathID pair) in field order, at most <paramref name="max"/>.</summary>
    public static IReadOnlyList<AssetReference> Collect(AssetTypeValueField root, int max = 500)
    {
        var found = new List<AssetReference>();
        Walk(root, "", found, max);
        return found;
    }

    private static void Walk(AssetTypeValueField field, string path, List<AssetReference> found, int max)
    {
        var children = field.Children;
        if (found.Count >= max || children is null || children.Count == 0) return;
        if (children.Count == 2 && children[0].FieldName == "m_FileID" && children[1].FieldName == "m_PathID")
        {
            var pathId = children[1].AsLong;
            if (pathId != 0) found.Add(new AssetReference(path, children[0].AsInt, pathId));
            return;
        }
        if (field.TemplateField.IsArray)
        {
            for (var i = 0; i < children.Count && found.Count < max; i++) Walk(children[i], $"{path}[{i}]", found, max);
            return;
        }
        foreach (var child in children)
        {
            // Unity vectors wrap their elements in an "Array" child; keep the owner's name for the elements.
            if (child.TemplateField.IsArray) Walk(child, path, found, max);
            else Walk(child, path.Length == 0 ? child.FieldName : $"{path}.{child.FieldName}", found, max);
            if (found.Count >= max) return;
        }
    }
}
