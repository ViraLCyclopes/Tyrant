using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tyrant.Framework
{
    /// <summary>
    /// Gives animals the models mods replace: the skin's model, else the species' replacement. Each LOD renderer gets the
    /// matching .tmesh as its sharedMesh; bones, bind poses (copied from the original mesh), materials and the game's growth
    /// shape keys stay. A model that cannot be loaded is logged once and the animal keeps its own.
    /// </summary>
    internal static class ModelModule
    {
        private static ModelTable _table = ModelTable.Build(Array.Empty<LoadedMod>());
        private static readonly Dictionary<string, Mesh?> Built = new Dictionary<string, Mesh?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Meshes this module put on renderers (by instance id): the .tmesh each came from and the game mesh it replaced.</summary>
        private static readonly Dictionary<int, (string Path, Mesh Original)> Ours = new Dictionary<int, (string, Mesh)>();
        private static readonly HashSet<string> Announced = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<Type, PropertyInfo?> BaseDataProperty = new Dictionary<Type, PropertyInfo?>();

        public static bool HasModels => _table.Count > 0;

        public static void Start(IReadOnlyList<LoadedMod> mods) => _table = ModelTable.Build(mods);

        public static void Apply(object animal, IReadOnlyList<Renderer> lods, string? skinKey)
        {
            if (!HasModels || lods.Count == 0) return;
            if (!_table.TryChoose(SpeciesOf(animal), skinKey, out var set))
            {
                RestoreOriginals(lods); // e.g. the animal changed to a skin without a model of its own
                return;
            }
            var meshes = new Mesh?[lods.Count];
            for (var i = 0; i < lods.Count; i++)
            {
                if (!(lods[i] is SkinnedMeshRenderer renderer) || renderer.sharedMesh == null) continue;
                var original = OriginalOf(renderer.sharedMesh);
                meshes[i] = MeshFor(set.LodPaths[Math.Min(i, set.LodPaths.Count - 1)], original, set.ModId);
                if (meshes[i] == null)
                {
                    RestoreOriginals(lods); // logged once; the animal keeps its own model on every LOD
                    return;
                }
            }
            for (var i = 0; i < lods.Count; i++)
                if (meshes[i] != null && lods[i] is SkinnedMeshRenderer renderer && !ReferenceEquals(renderer.sharedMesh, meshes[i]))
                    renderer.sharedMesh = meshes[i];
            if (Announced.Add(set.ModId + "|" + set.Target + "|" + skinKey))
                FrameworkMod.Log.Msg($"Replaced {set.Target}'s model ({set.ModId}{(skinKey == null ? "" : ", skin " + skinKey)}).");
        }

        private static Mesh OriginalOf(Mesh current) => Ours.TryGetValue(current.GetInstanceID(), out var ours) ? ours.Original : current;

        private static void RestoreOriginals(IReadOnlyList<Renderer> lods)
        {
            foreach (var lod in lods)
                if (lod is SkinnedMeshRenderer renderer && renderer.sharedMesh != null && Ours.TryGetValue(renderer.sharedMesh.GetInstanceID(), out var ours))
                    renderer.sharedMesh = ours.Original;
        }

        /// <summary>The .tmesh as a Unity mesh, built once per file and original mesh (bind poses and frame weights come from it).</summary>
        private static Mesh? MeshFor(string path, Mesh original, string modId)
        {
            var key = path + "|" + original.GetInstanceID();
            if (Built.TryGetValue(key, out var known)) return known;
            Mesh? mesh = null;
            try
            {
                TMesh t;
                using (var stream = File.OpenRead(path)) t = TMesh.Read(stream);
                if (t.BoneCount != original.bindposes.Length)
                    throw new TMeshException($"made for {t.BoneCount} bones, but the game's mesh has {original.bindposes.Length} (game updated? rebuild the model in Tyrant)");
                mesh = Build(t, original);
            }
            catch (Exception ex) when (ex is IOException || ex is TMeshException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                FrameworkMod.Log.Warning($"{modId}: model {Path.GetFileName(path)} could not be loaded ({ex.Message}); the animal keeps its own model.");
            }
            Built[key] = mesh;
            if (mesh != null) Ours[mesh.GetInstanceID()] = (path, original);
            return mesh;
        }

        private static Mesh Build(TMesh t, Mesh original)
        {
            var n = t.VertexCount;
            var mesh = new Mesh { name = t.Name, indexFormat = t.Index32 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(Vectors3(t.Positions));
            mesh.SetNormals(Vectors3(t.Normals));
            if (t.Uv0.Length == n * 2)
            {
                var uv = new List<Vector2>(n);
                for (var i = 0; i < n; i++) uv.Add(new Vector2(t.Uv0[i * 2], t.Uv0[i * 2 + 1]));
                mesh.SetUVs(0, uv);
            }
            if (t.Colors.Length == n * 4)
            {
                var colors = new List<Color>(n);
                for (var i = 0; i < n; i++) colors.Add(new Color(t.Colors[i * 4], t.Colors[i * 4 + 1], t.Colors[i * 4 + 2], t.Colors[i * 4 + 3]));
                mesh.SetColors(colors);
            }
            if (t.BoneIndices.Length == n * 4)
            {
                var weights = new BoneWeight[n];
                for (var i = 0; i < n; i++)
                    weights[i] = new BoneWeight
                    {
                        boneIndex0 = t.BoneIndices[i * 4], boneIndex1 = t.BoneIndices[i * 4 + 1], boneIndex2 = t.BoneIndices[i * 4 + 2], boneIndex3 = t.BoneIndices[i * 4 + 3],
                        weight0 = t.BoneWeights[i * 4], weight1 = t.BoneWeights[i * 4 + 1], weight2 = t.BoneWeights[i * 4 + 2], weight3 = t.BoneWeights[i * 4 + 3],
                    };
                mesh.boneWeights = weights;
            }
            mesh.bindposes = original.bindposes;
            mesh.subMeshCount = t.SubMeshStarts.Length;
            for (var s = 0; s < t.SubMeshStarts.Length; s++)
            {
                var indices = new int[t.SubMeshCounts[s]];
                Array.Copy(t.Indices, t.SubMeshStarts[s], indices, 0, indices.Length);
                mesh.SetTriangles(indices, s, false);
            }
            for (var k = 0; k < t.Shapes.Length; k++)
            {
                var weight = k < original.blendShapeCount ? original.GetBlendShapeFrameWeight(k, 0) : 100f;
                mesh.AddBlendShapeFrame(t.Shapes[k].Name, weight, Vectors3(t.Shapes[k].PositionDeltas).ToArray(), Vectors3(t.Shapes[k].NormalDeltas).ToArray(), null);
            }
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static List<Vector3> Vectors3(float[] values)
        {
            var list = new List<Vector3>(values.Length / 3);
            for (var i = 0; i + 2 < values.Length; i += 3) list.Add(new Vector3(values[i], values[i + 1], values[i + 2]));
            return list;
        }

        /// <summary>IAnimal.BaseData.speciesID, read by reflection so the repo needs no game assemblies.</summary>
        private static string? SpeciesOf(object animal)
        {
            var type = animal.GetType();
            if (!BaseDataProperty.TryGetValue(type, out var property)) BaseDataProperty[type] = property = AccessTools.Property(type, "BaseData");
            var data = property?.GetValue(animal, null);
            return data == null ? null : Traverse.Create(data).Field("speciesID").GetValue() as string;
        }
    }
}
