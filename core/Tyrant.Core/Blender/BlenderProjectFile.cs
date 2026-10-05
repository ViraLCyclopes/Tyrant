using System.Text.Json;
using Tyrant.Core.Errors;

namespace Tyrant.Core.Blender;

/// <summary>Where the model in a Blender project came from. Kind: "game" (a game skin), "skin" (a mod skin), "model" (a mod's species model).</summary>
public sealed record BlenderSource(string Kind, string Species, string? Skin, string? Mod);

/// <summary>Where Send to Tyrant puts the model: the mod, and the species model or one of its skins.</summary>
public sealed record BlenderDestination(string Mod, string Species, string? Skin);

/// <summary>One animal's colours as the PK Animal node group takes them (#rrggbb sRGB, null = none); Strength 0 leaves the textures as they are.</summary>
public sealed record BlenderColors(string? A, string? B, string? Secondary, string? Eye, float Strength, float Softness, float Hue, float Saturation, float Value);

/// <summary>A material's maps (slot → "textures/&lt;file&gt;.png", relative to the project) and how it is drawn.</summary>
public sealed record BlenderMaterial(bool Animal, float? Cutoff, IReadOnlyDictionary<string, string> Maps, BlenderColors? Colors);

/// <summary>A skeleton node's rest transform in glTF space (rotation x, y, z, w), for the growth slider's bone proportions.</summary>
public sealed record BlenderBoneRest(string Name, float[] Position, float[] Rotation, float[] Scale);

/// <summary>tyrant-blender.json: what the add-on needs to import a model, dress it like the game and send it back.</summary>
public sealed record BlenderProject(int Version, string Workspace, string Tyrant, string GameBuild, BlenderSource Source,
    BlenderDestination? Destination, IReadOnlyDictionary<string, BlenderMaterial> Materials, BlenderGrowth? Growth,
    IReadOnlyList<BlenderBoneRest> Rest, string? Blend, bool Lods)
{
    /// <summary>The kept .blend holds the model of an older game build (GameBuild is that build): Start fresh takes the new one.</summary>
    public bool GameChanged { get; init; }
}

public static class BlenderProjectFile
{
    public const string FileName = "tyrant-blender.json";
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static BlenderProject Read(string path)
    {
        try
        {
            var project = JsonSerializer.Deserialize<BlenderProject>(File.ReadAllText(path), Json);
            if (project is null || project.Version != CurrentVersion || project.Source is null || project.Materials is null || project.Rest is null)
                throw new JsonException("missing fields or another format version");
            return project;
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            throw new TyrantException(TyrantErrorCode.BlenderFailed,
                $"{path} could not be read ({ex.Message}). Open it again from Tyrant (Open in Blender → Start fresh).", FixAction.None, ex);
        }
    }

    public static void Write(string path, BlenderProject project)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(project, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
