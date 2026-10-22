using Tyrant.Core.Blender;

namespace Tyrant.Core.Tests;

/// <summary>
/// Writes a placeholder .fbx per glb (except the file names in <c>failing</c>) and records each batch; an FBX "converts" to
/// a copy of <see cref="FbxGlb"/>, counted in <see cref="FbxCalls"/>. No Blender.
/// </summary>
internal sealed class FakeModelConverter(params string[] failing) : IModelConverter
{
    public List<IReadOnlyList<(string Glb, string Fbx)>> Batches { get; } = [];

    /// <summary>The glb every FBX converts to; null: FbxToGlb is not supported.</summary>
    public string? FbxGlb { get; init; }

    public int FbxCalls { get; private set; }

    public void FbxToGlb(string fbx, string glb)
    {
        if (FbxGlb is null) throw new NotSupportedException();
        FbxCalls++;
        File.Copy(FbxGlb, glb, overwrite: true);
    }

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
