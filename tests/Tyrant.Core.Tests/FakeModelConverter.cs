using Tyrant.Core.Blender;

namespace Tyrant.Core.Tests;

/// <summary>Writes a placeholder .fbx per glb (except the file names in <c>failing</c>) and records each batch; no Blender.</summary>
internal sealed class FakeModelConverter(params string[] failing) : IModelConverter
{
    public List<IReadOnlyList<(string Glb, string Fbx)>> Batches { get; } = [];

    public void FbxToGlb(string fbx, string glb) => throw new NotSupportedException();

    public IReadOnlyDictionary<string, string> GlbToFbx(IReadOnlyList<(string Glb, string Fbx)> pairs)
    {
        Batches.Add(pairs);
        var failures = new Dictionary<string, string>();
        foreach (var (glb, fbx) in pairs)
            if (failing.Contains(Path.GetFileName(glb))) failures[glb] = "no armature";
            else File.WriteAllText(fbx, "fbx");
        return failures;
    }
}
