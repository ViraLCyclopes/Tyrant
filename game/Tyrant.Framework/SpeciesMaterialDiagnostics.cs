using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Tyrant.Framework
{
    // Observe the final material, after PK's asynchronous texture and preview setup.
    internal static class SpeciesMaterialDiagnostics
    {
        private sealed class Entry
        {
            public GameObject Root = null!;
            public Component Animal = null!;
            public string Id = "";
            public string Last = "";
            public int Changes;
            public int Unchanged;
        }

        /// <summary>Checks in a row without a change after which an animal is settled and no longer watched (one a second).</summary>
        private const int SettledAfter = 15;
        private static readonly List<Entry> Entries = new List<Entry>();
        private static float Next;

        public static void Track(GameObject root, Component animal, string id) =>
            Entries.Add(new Entry { Root = root, Animal = animal, Id = id });

        public static void Tick()
        {
            if (Entries.Count == 0 || Time.realtimeSinceStartup < Next) return;
            Next = Time.realtimeSinceStartup + 1f;
            for (var i = Entries.Count - 1; i >= 0; i--)
            {
                var entry = Entries[i];
                if (entry.Root == null || entry.Animal == null) { Entries.RemoveAt(i); continue; }
                if (entry.Changes >= 20 || entry.Unchanged >= SettledAfter) { Entries.RemoveAt(i); continue; } // settled: describing it costs a lot
                try
                {
                    var type = entry.Animal.GetType();
                    var baseMaterial = AccessTools.Property(type, "BaseMaterial")?.GetValue(entry.Animal, null) as Material;
                    var mode = AccessTools.Property(type, "Mode")?.GetValue(entry.Animal, null);
                    var renderers = entry.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    var state = "mode=" + mode + ", active=" + entry.Root.activeInHierarchy
                        + ", base=" + Describe(baseMaterial) + "; "
                        + string.Join("; ", renderers.Select(r => r.name + " enabled=" + r.enabled
                            + " mesh=" + (r.sharedMesh == null ? "missing" : r.sharedMesh.name)
                            + " material=" + string.Join(" / ", r.sharedMaterials.Select(Describe))));
                    if (state == entry.Last) { entry.Unchanged++; continue; }
                    entry.Unchanged = 0;
                    entry.Last = state;
                    entry.Changes++;
                    FrameworkMod.Log.Msg("Species render state " + entry.Id + ": " + state);
                }
                catch (Exception ex)
                {
                    entry.Changes = 20;
                    FrameworkMod.Log.Warning("Species material diagnostics: " + ex.GetBaseException().Message);
                }
            }
        }

        private static string Describe(Material? material)
        {
            if (material == null) return "missing";
            var shader = material.shader;
            return material.name + "#" + material.GetInstanceID()
                + " shader=" + (shader == null ? "missing" : shader.name + "#" + shader.GetInstanceID() + " supported=" + shader.isSupported)
                + " passes=" + material.passCount + " queue=" + material.renderQueue
                + " keywords=[" + string.Join(",", material.shaderKeywords.OrderBy(k => k)) + "]"
                + " passState=[" + string.Join(",", Enumerable.Range(0, material.passCount)
                    .Select(p => material.GetPassName(p) + "=" + material.GetShaderPassEnabled(material.GetPassName(p)))) + "]";
        }
    }
}
