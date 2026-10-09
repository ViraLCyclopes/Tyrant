using System.Text;
using System.Text.RegularExpressions;
using Tyrant.Core.Errors;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

public sealed record UnityPrefabImportResult(string Directory, string Prefab, string Report, int Assets);

/// <summary>Imports only the recovered prefab's GUID closure and binds scripts to Tyrant's local PK DLLs.</summary>
public static class UnityRecoveredAssets
{
    public const string ShaderPrefix = "Tyrant/Recovered/";
    public const string MaterialPrefix = "Tyrant/RecoveredMaterial/";
    private static readonly Regex GuidReference = new(@"\bguid: ([0-9a-fA-F]{32})\b", RegexOptions.Compiled);

    public static UnityPrefabImportResult Import(string exportedAssets, string project, string containerPath,
        string speciesId, string displayName, CancellationToken cancellationToken = default)
    {
        if (!ModId.IsValid(speciesId)) throw Invalid("Invalid species id.");
        if (File.Exists(Path.Combine(project, "Temp", "UnityLockfile"))) throw Invalid("Close this Unity project before importing a template.");
        var root = Path.GetFullPath(exportedAssets);
        var relative = containerPath.Replace('\\', '/');
        if (!relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) throw Invalid("The prefab has no Assets path.");
        var prefab = Path.GetFullPath(Path.Combine(root, relative[7..]));
        if (!prefab.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(prefab))
            throw Invalid("The recovery did not export the requested prefab: " + containerPath);
        var byGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var meta in Directory.EnumerateFiles(root, "*.meta", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = GuidReference.Match(File.ReadAllText(meta));
            if (match.Success && File.Exists(meta[..^5])) byGuid.Add(match.Groups[1].Value, meta[..^5]);
        }
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scriptGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in byGuid.Where(p => p.Value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            var assembly = Path.GetFileNameWithoutExtension(pair.Value);
            var authoring = UnityAuthoringReferences.Names.GetValueOrDefault(assembly, assembly);
            var meta = Path.Combine(project, "Assets", "PKReferences", authoring + ".dll.meta");
            if (!File.Exists(meta)) continue;
            var match = GuidReference.Match(File.ReadAllText(meta));
            if (!match.Success) throw Invalid("The local reference has no GUID: " + meta);
            mapping[pair.Key] = match.Groups[1].Value;
            scriptGuids.Add(pair.Key);
        }
        var pending = new Queue<string>();
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Enqueue(prefab);
        while (pending.TryDequeue(out var file))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!files.Add(file)) continue;
            if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) throw Invalid("A required script has no local PK reference: " + file);
            foreach (var text in new[] { TextAsset(file), File.ReadAllText(file + ".meta") })
            foreach (Match reference in GuidReference.Matches(text ?? ""))
            {
                var guid = reference.Groups[1].Value;
                if (guid.StartsWith("0000000000000000", StringComparison.Ordinal) || scriptGuids.Contains(guid)) continue;
                if (!byGuid.TryGetValue(guid, out var dependency)) throw Invalid("Unresolved recovered asset GUID: " + guid + " in " + file);
                if (!mapping.ContainsKey(guid)) mapping[guid] = Guid.NewGuid().ToString("N");
                pending.Enqueue(dependency);
            }
        }
        var folder = "Assets/Animals/" + speciesId + "/Template-" + Guid.NewGuid().ToString("N")[..12];
        var destination = Path.Combine(project, folder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(destination);
        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.Combine(destination, Path.GetRelativePath(root, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var text = TextAsset(file);
                if (text is null) File.Copy(file, target);
                else
                {
                    text = Rewrite(text, mapping);
                    if (file.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
                        text = Regex.Replace(text, "^(\\s*Shader\\s+\")([^\"]+)(\")", m => m.Groups[1].Value + ShaderPrefix + m.Groups[2].Value + m.Groups[3].Value);
                    if (file.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                        text = Regex.Replace(text, @"(?m)^  m_Name: ([^\r\n]*)", m => "  m_Name: " + System.Text.Json.JsonSerializer.Serialize(MaterialPrefix + m.Groups[1].Value.Trim().Trim('"')));
                    if (file == prefab) text = DisableRoot(text, displayName);
                    File.WriteAllText(target, text, new UTF8Encoding(false));
                }
                File.WriteAllText(target + ".meta", Rewrite(File.ReadAllText(file + ".meta"), mapping), new UTF8Encoding(false));
            }
            var asset = folder + "/" + Path.GetRelativePath(root, prefab).Replace('\\', '/');
            var report = Path.Combine(destination, "TyrantImport.txt");
            File.WriteAllText(report, "Species: " + displayName + "\nPrefab: " + asset + "\nAssets: " + files.Count
                + "\nSource: " + containerPath + "\nOpen the prefab in Unity 2022.3.62f3, then use Tyrant / Species prefab exporter."
                + "\nRecovered shaders are editor previews; Tyrant reconnects them to PK shaders when spawning."
                + "\nAnimations and species gameplay data continue to inherit from the selected AnimalData template."
                + "\nThis is a local authoring copy. Do not distribute the imported game DLLs.\n", new UTF8Encoding(false));
            return new UnityPrefabImportResult(project, asset, report, files.Count);
        }
        catch
        {
            // This unique folder was created by this call; existing authoring assets are never overwritten.
            Directory.Delete(destination, true);
            throw;
        }
    }

    internal static string Rewrite(string text, IReadOnlyDictionary<string, string> guids)
    {
        text = GuidReference.Replace(text, m => guids.TryGetValue(m.Groups[1].Value, out var guid) ? "guid: " + guid : m.Value);
        foreach (var pair in UnityAuthoringReferences.Names)
            text = Regex.Replace(text, @"(\basm:\s*)" + Regex.Escape(pair.Key) + @"(?=[,}\s]|$)", m => m.Groups[1].Value + pair.Value);
        return text;
    }

    internal static string DisableRoot(string text, string name)
    {
        // AssetRipper emits the main prefab root as its first GameObject document.
        var root = Regex.Match(text, @"(?ms)^--- !u!1 &[^\r\n]+\r?\nGameObject:\r?\n.*?(?=^--- |\z)");
        if (!root.Success) throw Invalid("Recovered prefab has no root GameObject.");
        var body = Regex.Replace(root.Value, @"(?m)^  m_IsActive: 1\r?$", "  m_IsActive: 0");
        // JSON string syntax is a valid quoted YAML scalar and safely escapes user names.
        body = Regex.Replace(body, @"(?m)^  m_Name: [^\r\n]*", _ => "  m_Name: " + System.Text.Json.JsonSerializer.Serialize(name));
        return text[..root.Index] + body + text[(root.Index + root.Length)..];
    }

    private static string? TextAsset(string file)
    {
        var extension = Path.GetExtension(file).ToLowerInvariant();
        if (extension == ".shader") return File.ReadAllText(file);
        using var stream = File.OpenRead(file);
        var bytes = new byte[5];
        return stream.Read(bytes) == 5 && Encoding.ASCII.GetString(bytes) == "%YAML" ? File.ReadAllText(file) : null;
    }

    private static TyrantException Invalid(string message) => new(TyrantErrorCode.ModInvalid, "Cannot import PK prefab: " + message);
}
