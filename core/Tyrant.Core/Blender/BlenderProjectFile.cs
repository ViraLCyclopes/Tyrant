using System.Text.Json;
using Tyrant.Core.Errors;

namespace Tyrant.Core.Blender;

/// <summary>Where the model in a Blender project came from. Kind: "game" (a game skin), "skin" (a mod skin), "model" (a mod's species model).</summary>
public sealed record BlenderSource(string Kind, string Species, string? Skin, string? Mod);

/// <summary>Where Send to Tyrant puts the model: the mod, and the species model or one of its skins.</summary>
public sealed record BlenderDestination(string Mod, string Species, string? Skin);

/// <summary>One animal's colours as the PK Animal node group takes them (#rrggbb sRGB, null = none); Strength 0 leaves the textures as they are.</summary>
public sealed record BlenderColors(string? A, string? B, string? Secondary, string? Eye, float Strength, float Softness, float Hue, float Saturation, float Value);

/// <summary>A material's maps (slot → "textures/&lt;file&gt;.png", relative to the project) and how it is drawn. Maps are the male's.</summary>
public sealed record BlenderMaterial(bool Animal, float? Cutoff, IReadOnlyDictionary<string, string> Maps, BlenderColors? Colors)
{
    /// <summary>The female's maps (each slot falls back to the male's), in textures/female/.</summary>
    public IReadOnlyDictionary<string, string> FemaleMaps { get; init; } = new Dictionary<string, string>();
}

/// <summary>One sex in the game (AnimalSkinData): how far its shape grows (1 = fully; a female Anax stops at 0.8) and its size.</summary>
public sealed record BlenderSex(float GrowthClamp, float Size);

public sealed record BlenderSexes(BlenderSex Male, BlenderSex Female);

/// <summary>A skeleton node's rest transform in glTF space (rotation x, y, z, w), for the growth slider's bone proportions.</summary>
public sealed record BlenderBoneRest(string Name, float[] Position, float[] Rotation, float[] Scale);

/// <summary>A rig edit on one bone as mod.json writes it (Unity space, parent-relative): move x,y,z; rotate x,y,z,w; scale x,y,z.</summary>
public sealed record BlenderRigOffset(float[] Move, float[] Rotate, float[] Scale)
{
    public static BlenderRigOffset From(Tyrant.Framework.Core.RigOffset o) =>
        new([o.Move.X, o.Move.Y, o.Move.Z], [o.Rotate.X, o.Rotate.Y, o.Rotate.Z, o.Rotate.W], [o.Scale.X, o.Scale.Y, o.Scale.Z]);
}

/// <summary>
/// For the add-on's rig edit warnings: bones the species' animations move and its growth positions or scales; GrowthSupported:
/// whether edits on growth bones work in game (else Send refuses them).
/// </summary>
public sealed record BlenderRigInfo(IReadOnlyList<string> ClipMoved, IReadOnlyList<string> GrowthMoved, IReadOnlyList<string> GrowthScaled,
    bool GrowthSupported, IReadOnlyList<string> Failures);

/// <summary>tyrant-blender.json: what the add-on needs to import a model, dress it like the game and send it back.</summary>
public sealed record BlenderProject(int Version, string Workspace, string Tyrant, string GameBuild, BlenderSource Source,
    BlenderDestination? Destination, IReadOnlyDictionary<string, BlenderMaterial> Materials, BlenderGrowth? Growth,
    IReadOnlyList<BlenderBoneRest> Rest, string? Blend, bool Lods)
{
    /// <summary>The kept .blend holds the model of an older game build (GameBuild is that build): Start fresh takes the new one.</summary>
    public bool GameChanged { get; init; }

    /// <summary>Start fresh was asked: the add-on keeps the model's scene as "(old)", imports into a new one, then clears this.</summary>
    public bool Fresh { get; init; }

    /// <summary>The sex Blender shows first ("male" or "female"; the Tyrant panel switches it).</summary>
    public string Sex { get; init; } = "male";

    /// <summary>The skin's growth limits and sizes; null without a data dump (both grow fully).</summary>
    public BlenderSexes? Sexes { get; init; }

    /// <summary>The game's growth shape keys (the first two of LOD 0: baby, juvenile), so the add-on can check them before Send.</summary>
    public IReadOnlyList<string> GrowthKeys { get; init; } = [];

    /// <summary>The game's IK chains (null = none, or a project written before Tyrant read them).</summary>
    public BlenderIk? Ik { get; init; }

    /// <summary>Whether Open in Blender builds the IK controls (the app's IK controls option, 'blender open --no-ik').</summary>
    public bool IkOnOpen { get; init; } = true;

    /// <summary>The rig edit the opened model wears in game (null for none).</summary>
    public IReadOnlyDictionary<string, BlenderRigOffset>? Rig { get; init; }

    /// <summary>True when the model's own skeleton already has the rig edit (a model made for it); false: the add-on applies it.</summary>
    public bool RigBaked { get; init; }

    /// <summary>The species' rig info for the warnings (null for objects, or when it could not be worked out).</summary>
    public BlenderRigInfo? RigInfo { get; init; }

    /// <summary>The species' animations for the add-on's list (null: none known yet, e.g. no data dump; the add-on asks Tyrant).</summary>
    public IReadOnlyList<Animation.AnimationInfo>? Animations { get; init; }

    /// <summary>Animation files (project-relative, written by Tyrant) the add-on loads as Actions when it opens the model.</summary>
    public IReadOnlyList<string>? AnimationFiles { get; init; }
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
