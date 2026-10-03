using System.Text;
using System.Text.Json;
using Tyrant.Core.Catalog;

namespace Tyrant.Core.Tests;

/// <summary>Encodes a synthetic Addressables 1.x catalog.json (same binary layout the game uses).</summary>
public static class CatalogFixture
{
    public const string BundleProvider = "UnityEngine.ResourceManagement.ResourceProviders.AssetBundleProvider";
    public const string AssetProvider = "UnityEngine.ResourceManagement.ResourceProviders.BundledAssetProvider";
    public const string BundleType = "UnityEngine.ResourceManagement.ResourceProviders.IAssetBundleResource";

    public sealed record Entry(string InternalId, string Provider, string ResourceType, string[] Keys, int[] Dependencies);

    public static Entry Bundle(string relativePath) =>
        new(AddressablesCatalog.RuntimePathToken + "\\" + relativePath.Replace('/', '\\'), BundleProvider, BundleType, [relativePath], []);

    public static string Build(params Entry[] entries)
    {
        var providers = entries.Select(e => e.Provider).Distinct().ToList();
        var types = entries.Select(e => e.ResourceType).Distinct().ToList();

        var bucketKeys = new List<string>();
        var bucketEntries = new List<List<int>>();
        int KeyIndex(string key)
        {
            var i = bucketKeys.IndexOf(key);
            if (i >= 0) return i;
            bucketKeys.Add(key);
            bucketEntries.Add([]);
            return bucketKeys.Count - 1;
        }

        var primary = new int[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            foreach (var key in entries[i].Keys) bucketEntries[KeyIndex(key)].Add(i);
            primary[i] = KeyIndex(entries[i].Keys[0]);
        }
        var dependencyKey = new int[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            if (entries[i].Dependencies.Length == 0) { dependencyKey[i] = -1; continue; }
            dependencyKey[i] = KeyIndex($"deps-of-{i}");
            bucketEntries[dependencyKey[i]].AddRange(entries[i].Dependencies);
        }

        var keyStream = new MemoryStream();
        var keyWriter = new BinaryWriter(keyStream);
        keyWriter.Write(bucketKeys.Count);
        var keyOffsets = new int[bucketKeys.Count];
        for (var k = 0; k < bucketKeys.Count; k++)
        {
            keyOffsets[k] = (int)keyStream.Position;
            var bytes = Encoding.ASCII.GetBytes(bucketKeys[k]);
            keyWriter.Write((byte)0); // AsciiString
            keyWriter.Write(bytes.Length);
            keyWriter.Write(bytes);
        }

        var bucketStream = new MemoryStream();
        var bucketWriter = new BinaryWriter(bucketStream);
        bucketWriter.Write(bucketKeys.Count);
        for (var k = 0; k < bucketKeys.Count; k++)
        {
            bucketWriter.Write(keyOffsets[k]);
            bucketWriter.Write(bucketEntries[k].Count);
            foreach (var e in bucketEntries[k]) bucketWriter.Write(e);
        }

        var entryStream = new MemoryStream();
        var entryWriter = new BinaryWriter(entryStream);
        entryWriter.Write(entries.Length);
        for (var i = 0; i < entries.Length; i++)
        {
            entryWriter.Write(i);                                   // internal id index
            entryWriter.Write(providers.IndexOf(entries[i].Provider));
            entryWriter.Write(dependencyKey[i]);
            entryWriter.Write(0);                                   // dependency hash
            entryWriter.Write(-1);                                  // extra data index
            entryWriter.Write(primary[i]);
            entryWriter.Write(types.IndexOf(entries[i].ResourceType));
        }

        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["m_LocatorId"] = "AddressablesMainContentCatalog",
            ["m_ProviderIds"] = providers,
            ["m_InternalIds"] = entries.Select(e => e.InternalId).ToList(),
            ["m_KeyDataString"] = Convert.ToBase64String(keyStream.ToArray()),
            ["m_BucketDataString"] = Convert.ToBase64String(bucketStream.ToArray()),
            ["m_EntryDataString"] = Convert.ToBase64String(entryStream.ToArray()),
            ["m_ExtraDataString"] = "",
            ["m_resourceTypes"] = types.Select(t => new Dictionary<string, string>
            {
                ["m_AssemblyName"] = "UnityEngine.CoreModule",
                ["m_ClassName"] = t,
            }).ToList(),
        });
    }
}
