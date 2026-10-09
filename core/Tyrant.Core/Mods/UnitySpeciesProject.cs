using Tyrant.Core.Errors;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

public static class UnitySpeciesProject
{
    public static string Create(Workspace workspace, string modId, string templateDirectory)
    {
        if (!ModId.IsValid(modId)) throw new TyrantException(TyrantErrorCode.ModInvalid, "Invalid mod id.");
        var target = Path.Combine(workspace.Dir, "authoring", modId, "Unity");
        var exists = File.Exists(Path.Combine(target, "ProjectSettings", "ProjectVersion.txt"));
        if (!File.Exists(Path.Combine(templateDirectory, "ProjectSettings", "ProjectVersion.txt")))
            throw new TyrantException(TyrantErrorCode.ModInvalid, "The Unity authoring template is missing from this Tyrant installation.");
        foreach (var source in Directory.EnumerateFiles(templateDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(templateDirectory, source);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            // Refresh Tyrant-owned tooling, preserving authored animals, DLL GUIDs and project settings.
            var tooling = relative.Replace('\\', '/').StartsWith("Assets/Tyrant/Editor/", StringComparison.Ordinal);
            if (!File.Exists(destination)) File.Copy(source, destination);
            else if (exists && tooling && !File.Exists(Path.Combine(target, "Temp", "UnityLockfile"))) File.Copy(source, destination, true);
        }
        return target;
    }
}
