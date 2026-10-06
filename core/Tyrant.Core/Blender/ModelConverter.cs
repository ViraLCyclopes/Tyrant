using Tyrant.Core.Errors;

namespace Tyrant.Core.Blender;

/// <summary>Converts model files Tyrant does not read or write itself (FBX) to and from glb.</summary>
public interface IModelConverter
{
    /// <summary>Writes the FBX's meshes, armature, shape keys and material names as a glb; throws BlenderFailed with Blender's last lines.</summary>
    void FbxToGlb(string fbx, string glb);

    /// <summary>Converts each glb to its FBX in one go; returns the failures (glb path → why), empty when all converted.</summary>
    IReadOnlyDictionary<string, string> GlbToFbx(IReadOnlyList<(string Glb, string Fbx)> pairs);
}

/// <summary>
/// The user's Blender converts, in the background (--factory-startup: none of the user's preferences or add-ons, nothing
/// saved), from a script written to a temporary file for the run.
/// </summary>
public sealed class BlenderModelConverter(IBlenderProcess process, string blenderExe, IReadOnlyDictionary<string, string>? env = null) : IModelConverter
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    public void FbxToGlb(string fbx, string glb)
    {
        if (File.Exists(glb)) File.Delete(glb);
        var run = RunScript(Scripts.FbxToGlb, [Path.GetFullPath(fbx), Path.GetFullPath(glb)]);
        if (run.ExitCode != 0 || !File.Exists(glb))
            throw new TyrantException(TyrantErrorCode.BlenderFailed, $"Blender could not convert {Path.GetFileName(fbx)}: {Tail(run.Output)}");
    }

    public IReadOnlyDictionary<string, string> GlbToFbx(IReadOnlyList<(string Glb, string Fbx)> pairs)
    {
        var failures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (pairs.Count == 0) return failures;
        // An FBX left by an earlier export must never pass for this one's (Blender killed or crashed before reaching it).
        foreach (var (_, fbx) in pairs)
            if (File.Exists(fbx)) File.Delete(fbx);
        var run = RunScript(Scripts.GlbToFbx, [.. pairs.SelectMany(p => new[] { Path.GetFullPath(p.Glb), Path.GetFullPath(p.Fbx) })]);
        var lines = run.Output.Split('\n', StringSplitOptions.TrimEntries);
        for (var i = 0; i < pairs.Count; i++)
        {
            var failed = lines.FirstOrDefault(l => l.StartsWith($"TYRANT-FBX-FAIL {i} ", StringComparison.Ordinal));
            if (failed is not null) failures[pairs[i].Glb] = failed[$"TYRANT-FBX-FAIL {i} ".Length..];
            else if (!lines.Contains($"TYRANT-FBX-OK {i}") || !File.Exists(pairs[i].Fbx))
            {
                if (File.Exists(pairs[i].Fbx)) File.Delete(pairs[i].Fbx); // half written when Blender stopped
                failures[pairs[i].Glb] = $"Blender did not finish it ({Tail(run.Output)})";
            }
        }
        return failures;
    }

    private BlenderRun RunScript(string script, IReadOnlyList<string> args)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tyrant-convert-{Guid.NewGuid():N}.py");
        File.WriteAllText(path, script);
        try
        {
            return process.Run(blenderExe, ["-b", "--factory-startup", "--python", path, "--", .. args], env, Timeout);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Tail(string output) =>
        string.Join(" ", output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(3)) is { Length: > 0 } tail ? tail : "no output";

    internal static class Scripts
    {
        public const string FbxToGlb = """
            import os, sys, traceback
            import bpy
            ok = False
            try:
                src, out = sys.argv[sys.argv.index("--") + 1:][:2]
                bpy.ops.wm.read_factory_settings(use_empty=True)
                bpy.ops.import_scene.fbx(filepath=src, ignore_leaf_bones=True, automatic_bone_orientation=False, use_custom_normals=True)
                if not any(o.type == "MESH" for o in bpy.data.objects):
                    raise RuntimeError("the FBX has no mesh")
                bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", use_selection=False, export_skins=True, export_morph=True,
                                          export_morph_normal=True, export_animations=False, export_materials="VIEWPORT", export_yup=True,
                                          export_rest_position_armature=True, export_apply=False)
                print("TYRANT-CONVERTED")
                ok = True
            except Exception:
                traceback.print_exc()
            sys.stdout.flush()
            os._exit(0 if ok else 1)
            """;

        public const string GlbToFbx = """
            import os, sys
            import bpy
            args = sys.argv[sys.argv.index("--") + 1:]
            for i in range(0, len(args) - 1, 2):
                glb, fbx = args[i], args[i + 1]
                try:
                    bpy.ops.wm.read_factory_settings(use_empty=True)
                    bpy.ops.import_scene.gltf(filepath=glb, disable_bone_shape=True, bone_heuristic="BLENDER", merge_vertices=True)
                    # Animations (glTF animations become Actions) go as FBX takes, one per Action, keyed as they are.
                    bpy.ops.export_scene.fbx(filepath=fbx, use_selection=False, add_leaf_bones=False, primary_bone_axis="Y",
                                             secondary_bone_axis="X", use_mesh_modifiers=False, bake_anim=bool(bpy.data.actions),
                                             bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, path_mode="RELATIVE")
                    print(f"TYRANT-FBX-OK {i // 2}", flush=True)
                except Exception as ex:
                    print(f"TYRANT-FBX-FAIL {i // 2} " + str(ex).replace("\n", " "), flush=True)
            sys.stdout.flush()
            os._exit(0)
            """;
    }
}
