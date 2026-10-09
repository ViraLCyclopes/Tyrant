using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tyrant.Framework
{
    /// <summary>Rebuild recovered materials from native PK material state, before Animal captures BaseMaterial.</summary>
    internal static class RecoveredMaterials
    {
        internal const string ShaderPrefix = "Tyrant/Recovered/";
        internal const string MaterialPrefix = "Tyrant/RecoveredMaterial/";

        public static int Restore(GameObject authored, GameObject template, Action<string>? log = null)
        {
            var originals = template.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Where(m => m != null && m.shader != null).Distinct().ToArray();
            var replacements = new Dictionary<Material, Material>();
            var renderers = authored.GetComponentsInChildren<Renderer>(true);
            // Resolve everything first; a failed recovery must not leave only some LODs changed.
            try
            {
                foreach (var source in renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct())
                {
                    var shaderName = source.shader != null ? source.shader.name : "";
                    var markedMaterial = source.name.StartsWith(MaterialPrefix, StringComparison.Ordinal);
                    var markedShader = shaderName.StartsWith(ShaderPrefix, StringComparison.Ordinal);
                    if (!markedMaterial && !markedShader) continue;
                    var name = markedMaterial ? source.name.Substring(MaterialPrefix.Length) : source.name;
                    var nativeShaderName = markedShader ? shaderName.Substring(ShaderPrefix.Length) : null;
                    var original = originals.FirstOrDefault(m => m.name == name && (nativeShaderName == null || m.shader.name == nativeShaderName))
                        ?? originals.FirstOrDefault(m => nativeShaderName != null && m.shader.name == nativeShaderName);
                    var shader = original != null ? original.shader : nativeShaderName == null ? null : Shader.Find(nativeShaderName);
                    if (shader == null || !shader.isSupported || shader.name.StartsWith(ShaderPrefix, StringComparison.Ordinal))
                        throw new InvalidOperationException("Cannot resolve a supported PK shader for recovered material '" + name + "'.");
                    // Changing shader on the dummy material retains its incompatible local keyword/pass state.
                    // Start with the actual game material, then transfer editable property values explicitly.
                    var recovered = original != null ? new Material(original) : new Material(shader);
                    replacements.Add(source, recovered);
                    recovered.name = name + " (Tyrant Unity)";
                    var copied = CopyProperties(source, recovered);
                    log?.Invoke("Recovered Unity material '" + name + "': " + shaderName + " -> " + shader.name
                        + ", supported=" + shader.isSupported + ", passes=" + recovered.passCount + ", properties=" + copied + ".");
                }
            }
            catch
            {
                foreach (var material in replacements.Values) UnityEngine.Object.Destroy(material);
                throw;
            }
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                    if (materials[i] != null && replacements.TryGetValue(materials[i], out var replacement))
                    { materials[i] = replacement; changed = true; }
                if (changed) renderer.sharedMaterials = materials;
            }
            return replacements.Count;
        }

        private static int CopyProperties(Material source, Material destination)
        {
            var shader = destination.shader;
            var copied = 0;
            for (var i = 0; i < shader.GetPropertyCount(); i++)
            {
                var name = shader.GetPropertyName(i);
                if (!source.HasProperty(name)) continue;
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color: destination.SetColor(name, source.GetColor(name)); break;
                    case ShaderPropertyType.Vector: destination.SetVector(name, source.GetVector(name)); break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range: destination.SetFloat(name, source.GetFloat(name)); break;
                    case ShaderPropertyType.Int: destination.SetInt(name, source.GetInt(name)); break;
                    case ShaderPropertyType.Texture:
                        destination.SetTexture(name, source.GetTexture(name));
                        destination.SetTextureScale(name, source.GetTextureScale(name));
                        destination.SetTextureOffset(name, source.GetTextureOffset(name));
                        break;
                    default: continue;
                }
                copied++;
            }
            return copied;
        }
    }
}
