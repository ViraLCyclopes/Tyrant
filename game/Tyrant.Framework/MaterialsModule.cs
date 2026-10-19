using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Tyrant.Framework.Core;
using UnityEngine;

namespace Tyrant.Framework
{
    /// <summary>
    /// Mod material settings and iridescence per animal, at draw time and never saved: a transpiler on
    /// AnimalMaterials.CalculateKeywords adds ExtraFeatures (the shader variant with iridescence) when the animal's strength is
    /// above 0, and postfixes on AnimalVisuals.SetAnimalMaterialVariation and AnimalGrowthManager.UpdateGrowth (the two places
    /// the game writes _IridescenceStrength) write the settings, ramp, mask, shaping and strength into the property block,
    /// read from the first LOD renderer and written to every LOD like the game's own edit scope.
    /// </summary>
    internal static class MaterialsModule
    {
        private const int ExtraFeatures = 8; // AnimalMaterialKeywords.ExtraFeatures

        private static MaterialTable _table = MaterialTable.Build(Array.Empty<LoadedMod>(), out _);
        private static bool _active;

        /// <summary>Material names each species / skin block sets, with the mod that set each (later in load order wins).</summary>
        private static readonly Dictionary<string, Dictionary<string, string>> SpeciesBlocks = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Dictionary<string, string>> SkinBlocks = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        private static readonly List<(string Id, string Directory)> ModDirectories = new List<(string, string)>();

        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
        private static readonly List<Renderer> Renderers = new List<Renderer>();
        private static readonly HashSet<string> Announced = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Texture2D?> Ramps = new Dictionary<string, Texture2D?>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture?> Masks = new Dictionary<string, Texture?>(StringComparer.Ordinal);
        private static readonly Dictionary<Type, Members> MembersByType = new Dictionary<Type, Members>();
        private static readonly Dictionary<(Type, string), FieldInfo?> Fields = new Dictionary<(Type, string), FieldInfo?>();
        private static readonly Dictionary<Type, PropertyInfo?> GrowthAnimal = new Dictionary<Type, PropertyInfo?>();

        public static bool HasMaterials { get; private set; }

        public static void Start(IReadOnlyList<LoadedMod> mods)
        {
            _table = MaterialTable.Build(mods, out var messages);
            foreach (var message in messages) FrameworkMod.Log.Warning(message);
            HasMaterials = _table.HasAny;
            foreach (var mod in mods)
            {
                var id = mod.Manifest.Id;
                ModDirectories.Add((id, mod.Directory));
                foreach (var model in mod.Manifest.Models)
                    if (model.Materials != null) Note(SpeciesBlocks, model.Target, model.Materials.Keys, id);
                foreach (var skin in mod.Manifest.Skins)
                    if (skin.Materials != null) Note(SkinBlocks, skin.Key(id), skin.Materials.Keys, id);
            }
        }

        public static void Patch(HarmonyLib.Harmony harmony)
        {
            _active = Hook(harmony, "PrehistoricKingdom.AnimalMaterials", m => m.Name == "CalculateKeywords" && m.IsStatic && m.GetParameters().Length == 2, nameof(KeywordTranspiler), transpiler: true)
                && Hook(harmony, "PrehistoricKingdom.AnimalVisuals", m => m.Name == "SetAnimalMaterialVariation", nameof(AfterVariation), transpiler: false)
                && Hook(harmony, "PrehistoricKingdom.AnimalGrowthManager", m => m.Name == "UpdateGrowth" && m.GetParameters().Length == 1, nameof(AfterGrowth), transpiler: false);
        }

        private static bool Hook(HarmonyLib.Harmony harmony, string typeName, Func<MethodInfo, bool> match, string patch, bool transpiler)
        {
            try
            {
                var type = AccessTools.TypeByName(typeName);
                var methods = type == null ? Array.Empty<MethodInfo>() : AccessTools.GetDeclaredMethods(type).Where(match).ToArray();
                if (methods.Length == 0)
                {
                    FrameworkMod.Log.Warning($"{typeName}: no method for mod materials was found (game updated?); mod materials and iridescence are off.");
                    return false;
                }
                var harmonyMethod = new HarmonyMethod(typeof(MaterialsModule), patch);
                foreach (var method in methods)
                {
                    if (transpiler) harmony.Patch(method, transpiler: harmonyMethod);
                    else harmony.Patch(method, postfix: harmonyMethod);
                }
                return true;
            }
            catch (Exception ex)
            {
                FrameworkMod.Log.Warning($"{typeName}: mod materials could not be hooked; mod materials and iridescence are off: {ex.GetBaseException().Message}");
                return false;
            }
        }

        // ---- hooks ----

        /// <summary>Before every return of CalculateKeywords: keywords = AddKeywords(keywords, animal).</summary>
        private static IEnumerable<CodeInstruction> KeywordTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var add = AccessTools.Method(typeof(MaterialsModule), nameof(AddKeywords));
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ret)
                {
                    var first = new CodeInstruction(OpCodes.Ldarg_0);
                    first.labels.AddRange(instruction.labels); // jumps to the return now run our code first
                    instruction.labels.Clear();
                    first.blocks.AddRange(instruction.blocks);
                    instruction.blocks.Clear();
                    yield return first;
                    yield return new CodeInstruction(OpCodes.Call, add);
                }
                yield return instruction;
            }
        }

        /// <summary>Adds ExtraFeatures when the animal's iridescence strength for its variant is above 0; never removes a keyword.</summary>
        private static int AddKeywords(int keywords, object animal)
        {
            if (!_active || animal == null) return keywords;
            try
            {
                var found = Resolve(animal);
                var strength = found?.Override.Iridescence?.Strength;
                if (strength != null && strength.For(SkinTypeOf(found!.Members, animal)) > 0) return keywords | ExtraFeatures;
            }
            catch (Exception ex)
            {
                Failed(ex);
            }
            return keywords;
        }

        // __args[0]: the animal (the extension method's 'this IAnimal').
        private static void AfterVariation(object[] __args) => Apply(__args.Length > 0 ? __args[0] : null);

        private static void AfterGrowth(object __instance)
        {
            if (!_active || __instance == null) return;
            object? animal;
            try
            {
                var type = __instance.GetType();
                if (!GrowthAnimal.TryGetValue(type, out var property)) GrowthAnimal[type] = property = AccessTools.Property(type, "Animal");
                animal = property?.GetValue(__instance, null);
            }
            catch (Exception ex)
            {
                Failed(ex);
                return;
            }
            Apply(animal);
        }

        // ---- applying ----

        public static void Apply(object? animal)
        {
            if (!_active || animal == null) return;
            try
            {
                ApplyTo(animal);
            }
            catch (Exception ex)
            {
                Failed(ex);
            }
        }

        private static void ApplyTo(object animal)
        {
            var found = Resolve(animal);
            if (found == null || !Lods(found.Members, animal)) return;
            var o = found.Override;
            var i = o.Iridescence;

            // Textures first: a failure there leaves the block untouched.
            var male = string.Equals(found.Members.Sex?.GetValue(animal, null)?.ToString(), "Male", StringComparison.Ordinal);
            var ramp = i?.Ramp == null ? null : RampTexture(i.Ramp);
            var mask = i?.Mask == null ? null : MaskTexture(i.Mask, found, male);

            Renderers[0].GetPropertyBlock(Block);
            foreach (var setting in o.Settings)
            {
                var v = setting.Value;
                if (v.Kind == MaterialValueKind.Color) Block.SetColor(setting.Key, new Color(v.Color.R, v.Color.G, v.Color.B, 1f)); // sRGB; Unity converts
                else if (v.Kind == MaterialValueKind.Vector) Block.SetVector(setting.Key, new Vector4(v.Vector[0], v.Vector[1], v.Vector[2], v.Vector[3]));
                else Block.SetFloat(setting.Key, v.Number);
            }
            if (i != null)
            {
                if (i.Scale is float scale) Block.SetFloat("_IridescenceScale", scale);
                if (i.Power is float power) Block.SetFloat("_IridescencePower", power);
                if (i.Bias is float bias) Block.SetFloat("_IridescenceBias", bias);
                if (i.Tiling != null || i.Offset != null)
                    Block.SetVector("_IridescenceMask_ST", new Vector4(i.Tiling?[0] ?? 1, i.Tiling?[1] ?? 1, i.Offset?[0] ?? 0, i.Offset?[1] ?? 0));
                if (ramp != null) Block.SetTexture("_IridescenceTexture", ramp);
                if (mask != null) Block.SetTexture("_IridescenceMask", mask);
                if (i.Strength != null)
                {
                    var strength = MaterialStrength.For(i.Strength.For(SkinTypeOf(found.Members, animal)), MaterialTable.EffectiveVariation(i), IdOf(found.Members, animal));
                    Block.SetFloat("_IridescenceStrength", strength * Block.GetFloat("_IntegumentMaturity")); // the game wrote it just before
                }
            }
            foreach (var renderer in Renderers) renderer.SetPropertyBlock(Block);

            if (Announced.Add("on|" + (found.SkinKey ?? found.Species)))
                FrameworkMod.Log.Msg($"materials on {found.Species}{(found.SkinKey == null ? "" : ", skin " + found.SkinKey)} ({found.ModId})");
        }

        /// <summary>What the animal wears: null when no mod changes its material.</summary>
        private static Resolved? Resolve(object animal)
        {
            var members = MembersOf(animal.GetType());
            if (!(members.BaseMaterial?.GetValue(animal, null) is Material material) || material == null) return null;
            var name = material.name;
            var data = members.BaseData?.GetValue(animal, null);
            var species = data == null ? null : FieldOf(data, "speciesID")?.GetValue(data) as string;
            var skinData = members.SkinData?.GetValue(animal, null);
            var resolved = new Resolved(members, species ?? "");
            if (SkinsModule.TryGet(skinData, out var modId, out var entry, out var directory))
                (resolved.SkinKey, resolved.SkinModId, resolved.Entry, resolved.Directory) = (entry.Key(modId), modId, entry, directory);
            if (SkinsModule.TryGetInherited(skinData, out var baseModId, out var baseEntry, out var baseDirectory))
                (resolved.InheritedKey, resolved.BaseModId, resolved.BaseEntry, resolved.BaseDirectory) = (baseEntry.Key(baseModId), baseModId, baseEntry, baseDirectory);

            ReportOtherNames(SkinBlocks, resolved.SkinKey, name);
            ReportOtherNames(SkinBlocks, resolved.InheritedKey, name);
            ReportOtherNames(SpeciesBlocks, species, name);

            var found = _table.For(species, resolved.SkinKey, resolved.InheritedKey, name);
            if (found == null) return null;
            resolved.Override = found;
            resolved.ModId = OwnerOf(SkinBlocks, resolved.SkinKey, name) ?? OwnerOf(SkinBlocks, resolved.InheritedKey, name) ?? OwnerOf(SpeciesBlocks, species, name) ?? "";
            return resolved;
        }

        // ---- textures ----

        /// <summary>The ramp texture (32×32 from stops or a thin film, or the mod's PNG), uncompressed and without mips; null when it cannot be made.</summary>
        private static Texture2D? RampTexture(RampSource ramp)
        {
            var stops = IridescenceRamp.Stops(ramp);
            if (stops == null && ramp.File == null) return null;
            var key = stops != null ? "stops|" + string.Join(",", stops) : "file|" + ramp.File;
            if (Ramps.TryGetValue(key, out var cached)) return cached;
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false, false) { name = "Tyrant iridescence ramp", hideFlags = HideFlags.DontUnloadUnusedAsset };
            try
            {
                if (stops != null)
                {
                    texture.LoadRawTextureData(IridescenceRamp.Pixels(stops));
                    texture.Apply(false, true);
                }
                else if (!texture.LoadImage(File.ReadAllBytes(ramp.File!), true))
                {
                    UnityEngine.Object.Destroy(texture);
                    texture = null;
                    FrameworkMod.Log.Warning($"{ModOf(ramp.File!)}: {Path.GetFileName(ramp.File)} is not a readable PNG; the iridescence keeps the material's own ramp.");
                }
                if (texture != null) texture.wrapMode = TextureWrapMode.Clamp;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is UnityException || ex is ArgumentException)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
                texture = null;
                FrameworkMod.Log.Warning($"{ModOf(ramp.File ?? "")}: the iridescence ramp {Path.GetFileName(ramp.File ?? "")} could not be made ({ex.Message}); the iridescence keeps the material's own ramp.");
            }
            Ramps[key] = texture; // a failure is remembered too, so it is reported once
            return texture;
        }

        private static Texture? MaskTexture(MaskSource mask, Resolved found, bool male)
        {
            if (mask.Everywhere) return Texture2D.whiteTexture;
            if (mask.File != null) return TextureCache.Get(new Replacement(ModOf(mask.File), "material mask", mask.File), SlotKind.Data);
            if (mask.From == null) return null;

            var skin = found.SkinKey ?? found.InheritedKey ?? found.Species;
            var key = $"{skin}|{(male ? "male" : "female")}|{mask.From}|{mask.Channel}|{mask.Invert}";
            if (Masks.TryGetValue(key, out var cached)) return cached;
            Texture? result = Texture2D.whiteTexture;
            var has = SlotFile(found.Entry, found.SkinModId, found.Directory, male, mask.From, out var source)
                || SlotFile(found.BaseEntry, found.BaseModId, found.BaseDirectory, male, mask.From, out source);
            if (!has) FrameworkMod.Log.Warning($"{skin}: its iridescence mask uses {mask.From}, which the skin has no file for; the whole body shimmers.");
            else if (source != null) // null: TextureCache reported the PNG
            {
                try
                {
                    result = Extract(source, mask.Channel, mask.Invert);
                }
                catch (Exception ex)
                {
                    FrameworkMod.Log.Warning($"{skin}: its iridescence mask could not be made from {mask.From} ({ex.GetBaseException().Message}); the whole body shimmers.");
                }
            }
            Masks[key] = result;
            return result;
        }

        /// <summary>True when the skin has a PNG for the adult slot for this sex; the texture is null when it could not be loaded.</summary>
        private static bool SlotFile(SkinEntry? entry, string? modId, string? directory, bool male, string slot, out Texture2D? texture)
        {
            texture = null;
            var files = entry == null ? null : male ? entry.Male : entry.Female;
            if (files == null || modId == null || directory == null || !files.TryGetValue(slot, out var file)) return false;
            var path = Path.Combine(directory, file);
            if (!ModPaths.IsInside(path, directory)) return false;
            texture = TextureCache.Get(new Replacement(modId, $"{entry!.Key(modId)}:{slot}", Path.GetFullPath(path)), SkinSlotNames.KindOf(slot));
            return true;
        }

        /// <summary>One channel of a (possibly compressed, unreadable) texture as a grey mask, through a linear render texture.</summary>
        private static Texture2D Extract(Texture source, char channel, bool invert)
        {
            int width = source.width, height = source.height;
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            var mask = new Texture2D(width, height, TextureFormat.RGBA32, true, true) { name = "Tyrant iridescence mask", hideFlags = HideFlags.DontUnloadUnusedAsset };
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                mask.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                mask.SetPixelData(MaskChannel.Extract(mask.GetPixelData<byte>(0).ToArray(), channel, invert), 0);
                mask.Apply(true, false);
                if (width % 4 == 0 && height % 4 == 0) mask.Compress(true);
                mask.Apply(false, true); // no longer readable: frees the CPU copy
                return mask;
            }
            catch
            {
                UnityEngine.Object.Destroy(mask);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        // ---- reflection and helpers ----

        private sealed class Members
        {
            public Members(Type type)
            {
                BaseData = AccessTools.Property(type, "BaseData");
                SkinData = AccessTools.Property(type, "SkinData");
                SkinType = AccessTools.Property(type, "SkinType");
                Sex = AccessTools.Property(type, "Sex");
                BaseMaterial = AccessTools.Property(type, "BaseMaterial");
                HasValidPersistentId = AccessTools.Property(type, "HasValidPersistentID");
                PersistentId = AccessTools.Property(type, "PersistentID");
                Lods = AccessTools.Property(type, "Lods");
            }

            public PropertyInfo? BaseData { get; }
            public PropertyInfo? SkinData { get; }
            public PropertyInfo? SkinType { get; }
            public PropertyInfo? Sex { get; }
            public PropertyInfo? BaseMaterial { get; }
            public PropertyInfo? HasValidPersistentId { get; }
            public PropertyInfo? PersistentId { get; }
            public PropertyInfo? Lods { get; }
        }

        private sealed class Resolved
        {
            public Resolved(Members members, string species)
            {
                Members = members;
                Species = species;
            }

            public Members Members { get; }
            public string Species { get; }
            public MaterialOverride Override { get; set; } = null!;
            public string ModId { get; set; } = "";
            public string? SkinKey { get; set; }
            public string? SkinModId { get; set; }
            public SkinEntry? Entry { get; set; }
            public string? Directory { get; set; }
            public string? InheritedKey { get; set; }
            public string? BaseModId { get; set; }
            public SkinEntry? BaseEntry { get; set; }
            public string? BaseDirectory { get; set; }
        }

        private static Members MembersOf(Type type)
        {
            if (!MembersByType.TryGetValue(type, out var members)) MembersByType[type] = members = new Members(type);
            return members;
        }

        private static FieldInfo? FieldOf(object target, string name)
        {
            var key = (target.GetType(), name);
            if (!Fields.TryGetValue(key, out var field)) Fields[key] = field = AccessTools.Field(key.Item1, name);
            return field;
        }

        /// <summary>IAnimal.SkinType as the game's AnimalSkinType number (0 base, 1 albino, 2 melanistic, 3 leucistic).</summary>
        private static int SkinTypeOf(Members members, object animal) =>
            members.SkinType?.GetValue(animal, null) is IConvertible value ? value.ToInt32(null) : 0;

        /// <summary>PersistentID.uniqueID when the animal has a valid one; null otherwise (then it gets the set strength exactly).</summary>
        private static ulong? IdOf(Members members, object animal)
        {
            if (!(members.HasValidPersistentId?.GetValue(animal, null) is bool valid) || !valid) return null;
            var id = members.PersistentId?.GetValue(animal, null);
            return id != null && FieldOf(id, "uniqueID")?.GetValue(id) is ulong unique ? unique : (ulong?)null;
        }

        /// <summary>IAnimal.Lods into <see cref="Renderers"/>; false when there are none.</summary>
        private static bool Lods(Members members, object animal)
        {
            Renderers.Clear();
            if (members.Lods?.GetValue(animal, null) is IEnumerable items)
                foreach (var item in items)
                    if (item is Renderer renderer && renderer != null) Renderers.Add(renderer);
            return Renderers.Count > 0;
        }

        private static void Note(Dictionary<string, Dictionary<string, string>> blocks, string owner, IEnumerable<string> materials, string modId)
        {
            if (!blocks.TryGetValue(owner, out var names)) blocks[owner] = names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var material in materials) names[material] = modId;
        }

        private static string? OwnerOf(Dictionary<string, Dictionary<string, string>> blocks, string? owner, string material) =>
            owner != null && blocks.TryGetValue(owner, out var names) && names.TryGetValue(material, out var modId) ? modId : null;

        /// <summary>Part A changes only the animal's own material: any other name in a block is reported once.</summary>
        private static void ReportOtherNames(Dictionary<string, Dictionary<string, string>> blocks, string? owner, string material)
        {
            if (owner == null || !blocks.TryGetValue(owner, out var names)) return;
            foreach (var pair in names)
                if (!string.Equals(pair.Key, material, StringComparison.Ordinal) && Announced.Add("name|" + owner + "|" + pair.Key))
                    FrameworkMod.Log.Warning($"{pair.Value}: \"{pair.Key}\" is not this animal's material ({owner}); extra materials come in a later Tyrant.");
        }

        /// <summary>The mod whose folder holds a file (its cooked pack is looked up by that mod's id).</summary>
        private static string ModOf(string path)
        {
            var best = "";
            var length = -1;
            foreach (var (id, directory) in ModDirectories)
                if (directory.Length > length && ModPaths.IsInside(path, directory)) (best, length) = (id, directory.Length);
            return best;
        }

        private static void Failed(Exception ex)
        {
            var message = ex.GetBaseException().Message;
            if (Announced.Add("failed|" + message))
                FrameworkMod.Log.Error("Mod materials failed for an animal; it keeps its vanilla look: " + message);
        }
    }
}
