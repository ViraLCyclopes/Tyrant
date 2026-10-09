using Mono.Cecil;
using Tyrant.Core.Errors;

namespace Tyrant.Core.Mods;

/// <summary>Local game DLLs with names Unity can import as component plugins.</summary>
public static class UnityAuthoringReferences
{
    internal static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["Assembly-CSharp"] = "PK.Authoring.Gameplay",
        ["Assembly-CSharp-firstpass"] = "PK.Authoring.Firstpass",
    };

    public static void Prepare(string project, string managed)
    {
        var destination = Path.Combine(project, "Assets", "PKReferences");
        if (File.Exists(Path.Combine(destination, "PK.Authoring.Gameplay.dll"))) return;
        if (File.Exists(Path.Combine(project, "Temp", "UnityLockfile")))
            throw new TyrantException(TyrantErrorCode.ModInvalid, "Close the Unity project before preparing its PK references.");
        if (!File.Exists(Path.Combine(managed, "Assembly-CSharp.dll")))
            throw new TyrantException(TyrantErrorCode.ModInvalid, "PK's managed assemblies were not found.");
        Directory.CreateDirectory(destination);
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managed);
        var files = Directory.GetFiles(managed, "*.dll").Where(Include).OrderBy(f => Path.GetFileName(f) == "Assembly-CSharp.dll" ? 1 : 0).ToList();
        foreach (var file in files)
        {
            using var assembly = AssemblyDefinition.ReadAssembly(file, new ReaderParameters { AssemblyResolver = resolver });
            var originalName = assembly.Name.Name;
            var renamed = Names.TryGetValue(originalName, out var authoringName);
            var changed = renamed;
            if (renamed) { assembly.Name.Name = authoringName!; assembly.MainModule.Name = authoringName + ".dll"; }
            foreach (var reference in assembly.MainModule.AssemblyReferences)
                if (Names.TryGetValue(reference.Name, out var newName)) { reference.Name = newName; changed = true; }
            var target = Path.Combine(destination, renamed ? authoringName + ".dll" : Path.GetFileName(file));
            var temp = target + ".tmp";
            try
            {
                if (changed) assembly.Write(temp);
                else File.Copy(file, temp, true);
                File.Move(temp, target, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            // Preserve GUIDs for projects made with the initial direct-DLL importer.
            var oldTarget = Path.Combine(destination, Path.GetFileName(file));
            if (renamed && File.Exists(oldTarget + ".meta") && !File.Exists(target + ".meta")) File.Copy(oldTarget + ".meta", target + ".meta");
            if (!File.Exists(target + ".meta")) File.WriteAllText(target + ".meta", Meta());
            if (renamed && oldTarget != target)
            {
                if (File.Exists(oldTarget)) File.Delete(oldTarget);
                if (File.Exists(oldTarget + ".meta")) File.Delete(oldTarget + ".meta");
            }
        }
    }

    private static bool Include(string file)
    {
        var name = Path.GetFileName(file);
        if (new[] { "UnityEngine.UI.dll", "System.Runtime.CompilerServices.Unsafe.dll", "System.IO.Hashing.dll" }.Contains(name)) return true;
        return !new[] { "UnityEngine", "UnityEditor", "System.", "mscorlib.", "netstandard.", "Mono.", "Microsoft." }.Any(p => name.StartsWith(p, StringComparison.Ordinal));
    }

    private static string Meta() => "fileFormatVersion: 2\nguid: " + Guid.NewGuid().ToString("N") +
        "\nPluginImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  iconMap: {}\n  executionOrder: {}\n  defineConstraints: []\n  isPreloaded: 0\n  isOverridable: 0\n  isExplicitlyReferenced: 0\n  validateReferences: 1\n  platformData:\n  - first:\n      Any:\n    second:\n      enabled: 1\n      settings: {}\n  - first:\n      Editor: Editor\n    second:\n      enabled: 1\n      settings:\n        DefaultValueInitialized: true\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
}
