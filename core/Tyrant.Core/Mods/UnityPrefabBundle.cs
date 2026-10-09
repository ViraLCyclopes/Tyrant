using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Tyrant.Core.Errors;

namespace Tyrant.Core.Mods;

/// <summary>Restores PK's real script assembly identities in Unity-authored bundles.</summary>
public static class UnityPrefabBundle
{
    public static void CopyForGame(string source, string destination) => Rewrite(source, destination,
        UnityAuthoringReferences.Names.ToDictionary(p => p.Value, p => p.Key));

    internal static void Rewrite(string source, string destination, IReadOnlyDictionary<string, string> names)
    {
        var manager = new AssetsManager();
        try
        {
            var bundle = manager.LoadBundleFile(source, true);
            for (var i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
            {
                if (!bundle.file.IsAssetsFile(i)) continue;
                var file = manager.LoadAssetsFileFromBundle(bundle, i, false);
                if (!file.file.Metadata.TypeTreeEnabled) throw new InvalidDataException("Prefab bundles must include type trees.");
                if (file.file.Metadata.TargetPlatform != 19) throw new InvalidDataException("Build the prefab bundle for Windows 64-bit.");
                foreach (var info in file.file.GetAssetsOfType(AssetClassID.MonoScript))
                {
                    var field = manager.GetBaseField(file, info);
                    field["m_AssemblyName"].AsString = Rename(field["m_AssemblyName"].AsString, names);
                    info.SetNewData(field);
                }
                // SerializeReference records carry assembly names both in the metadata and in their object data.
                foreach (var info in file.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
                {
                    var field = manager.GetBaseField(file, info);
                    RewriteFields(field, names);
                    info.SetNewData(field);
                }
                // Keep original lookup keys until every managed-reference payload has been parsed.
                foreach (var type in file.file.Metadata.RefTypes)
                    type.TypeReference.AsmName = Rename(type.TypeReference.AsmName, names);
                bundle.file.BlockAndDirInfo.DirectoryInfos[i].SetNewData(file.file);
            }
            using var unpacked = new MemoryStream();
            using var unpackedWriter = new AssetsFileWriter(unpacked);
            bundle.file.Write(unpackedWriter);
            unpacked.Position = 0;
            var rewritten = new AssetBundleFile();
            rewritten.Read(new AssetsFileReader(unpacked));
            try
            {
                using var writer = new AssetsFileWriter(destination);
                rewritten.Pack(writer, AssetBundleCompressionType.LZ4);
            }
            finally { rewritten.Close(); }
        }
        catch (Exception ex) when (ex is not TyrantException)
        {
            if (File.Exists(destination)) File.Delete(destination);
            throw new TyrantException(TyrantErrorCode.ModInvalid, "Cannot prepare Unity prefab bundle: " + ex.Message, FixAction.None, ex);
        }
        finally { manager.UnloadAllAssetsFiles(); manager.UnloadAllBundleFiles(); }
    }

    internal static void RewriteFields(AssetTypeValueField field, IReadOnlyDictionary<string, string> names)
    {
        if (field.IsDummy) return;
        if (field.Value?.ValueType == AssetValueType.ManagedReferencesRegistry)
        {
            foreach (var reference in field.AsManagedReferencesRegistry.references)
            {
                reference.type.AsmName = Rename(reference.type.AsmName, names);
                if (reference.data != null) RewriteFields(reference.data, names);
            }
        }
        if (field.TypeName == "string" && (field.FieldName == "asm" || field.FieldName == "assembly" || field.FieldName == "m_AssemblyName"))
            field.AsString = Rename(field.AsString, names);
        foreach (var child in field.Children ?? []) RewriteFields(child, names);
    }

    private static string Rename(string name, IReadOnlyDictionary<string, string> names)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var suffix = name.EndsWith(".dll", StringComparison.Ordinal) ? ".dll" : "";
        var simple = suffix.Length == 0 ? name : name[..^4];
        return names.TryGetValue(simple, out var replacement) ? replacement + suffix : name;
    }
}
