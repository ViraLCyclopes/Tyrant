using System.Globalization;
using System.Text.Json;
using PK.Core.Errors;

namespace PK.Core.Catalog;

public sealed record CatalogEntry(
    int Index, string InternalId, string Provider, string ResourceType, IReadOnlyList<string> Keys, IReadOnlyList<int> Dependencies)
{
    public bool IsBundle => Provider.EndsWith("AssetBundleProvider", StringComparison.Ordinal);

    /// <summary>Type name without namespace, e.g. "Texture2D" for "UnityEngine.Texture2D".</summary>
    public string ShortResourceType => ResourceType[(ResourceType.LastIndexOf('.') + 1)..];
}

/// <summary>Reader for an Addressables 1.x content catalog (catalog.json).</summary>
public sealed class AddressablesCatalog
{
    public const string RuntimePathToken = "{UnityEngine.AddressableAssets.Addressables.RuntimePath}";

    private AddressablesCatalog(IReadOnlyList<CatalogEntry> entries) => Entries = entries;

    public IReadOnlyList<CatalogEntry> Entries { get; }

    public static AddressablesCatalog Load(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Invalid($"Cannot read the Addressables catalog '{path}': {ex.Message}", ex);
        }
        return Parse(json);
    }

    public static AddressablesCatalog Parse(string json)
    {
        try
        {
            return ParseCore(json);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException
                                       or IndexOutOfRangeException or KeyNotFoundException or InvalidOperationException)
        {
            throw Invalid($"The Addressables catalog could not be read: {ex.Message}", ex);
        }
    }

    /// <summary>Path of a bundle relative to StreamingAssets/aa with '/' separators; null if the id is not a bundle path.</summary>
    public static string? BundleRelativePath(string internalId) =>
        internalId.StartsWith(RuntimePathToken, StringComparison.Ordinal)
            ? internalId[RuntimePathToken.Length..].TrimStart('\\', '/').Replace('\\', '/')
            : null;

    private static AddressablesCatalog ParseCore(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var internalIds = root.GetProperty("m_InternalIds").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
        var providers = root.GetProperty("m_ProviderIds").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
        var types = root.GetProperty("m_resourceTypes").EnumerateArray()
            .Select(e => e.GetProperty("m_ClassName").GetString() ?? "").ToArray();
        var keyData = Convert.FromBase64String(root.GetProperty("m_KeyDataString").GetString() ?? "");
        var bucketData = Convert.FromBase64String(root.GetProperty("m_BucketDataString").GetString() ?? "");
        var entryData = Convert.FromBase64String(root.GetProperty("m_EntryDataString").GetString() ?? "");

        var offset = 0;
        var bucketCount = CatalogBinaryReader.ReadInt32(bucketData, ref offset);
        if (bucketCount < 0) throw new FormatException("Negative bucket count.");
        var bucketKeys = new string[bucketCount];
        var bucketEntries = new int[bucketCount][];
        for (var b = 0; b < bucketCount; b++)
        {
            var keyOffset = CatalogBinaryReader.ReadInt32(bucketData, ref offset);
            var count = CatalogBinaryReader.ReadInt32(bucketData, ref offset);
            if (count < 0) throw new FormatException($"Bucket {b} has a negative entry count.");
            var members = new int[count];
            for (var e = 0; e < count; e++) members[e] = CatalogBinaryReader.ReadInt32(bucketData, ref offset);
            bucketEntries[b] = members;
            bucketKeys[b] = Convert.ToString(CatalogBinaryReader.ReadObject(keyData, keyOffset), CultureInfo.InvariantCulture) ?? "";
        }

        offset = 0;
        var entryCount = CatalogBinaryReader.ReadInt32(entryData, ref offset);
        if (entryCount < 0) throw new FormatException("Negative entry count.");
        var keys = new List<string>[entryCount];
        for (var i = 0; i < entryCount; i++) keys[i] = [];
        for (var b = 0; b < bucketCount; b++)
            foreach (var e in bucketEntries[b])
                keys[e].Add(bucketKeys[b]);

        var entries = new CatalogEntry[entryCount];
        for (var i = 0; i < entryCount; i++)
        {
            var internalId = CatalogBinaryReader.ReadInt32(entryData, ref offset);
            var provider = CatalogBinaryReader.ReadInt32(entryData, ref offset);
            var dependencyKey = CatalogBinaryReader.ReadInt32(entryData, ref offset);
            CatalogBinaryReader.ReadInt32(entryData, ref offset); // dependency hash
            CatalogBinaryReader.ReadInt32(entryData, ref offset); // extra data index
            CatalogBinaryReader.ReadInt32(entryData, ref offset); // primary key
            var type = CatalogBinaryReader.ReadInt32(entryData, ref offset);
            entries[i] = new CatalogEntry(i, internalIds[internalId], providers[provider], types[type], keys[i],
                dependencyKey >= 0 ? bucketEntries[dependencyKey] : []);
        }
        return new AddressablesCatalog(entries);
    }

    private static PkException Invalid(string message, Exception inner) =>
        new(PkErrorCode.CatalogInvalid, message, FixAction.None, inner);
}
