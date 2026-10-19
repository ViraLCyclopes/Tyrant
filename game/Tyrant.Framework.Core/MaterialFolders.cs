using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace System.Runtime.CompilerServices
{
    // netstandard2.0 has no IsExternalInit; the compiler needs it for records.
    internal static class IsExternalInit
    {
    }
}

namespace Tyrant.Framework.Core
{
    public enum MaterialOwnerKind { Skin, Species, VanillaSkin }

    /// <summary>Who a materials.json belongs to: a mod skin (SkinId), a species (Species), or a vanilla skin (Species and SkinName).</summary>
    public sealed record MaterialOwner(MaterialOwnerKind Kind, string? SkinId, string? Species, string? SkinName);

    /// <summary>
    /// A mod's materials live beside its textures, one materials.json per owner: materials/skins/&lt;skinId&gt;/,
    /// materials/species/&lt;species&gt;/ and materials/vanilla-skins/&lt;species&gt;-&lt;skin&gt;/. mod.json does not list them.
    /// </summary>
    public static class MaterialFolders
    {
        public const string Root = "materials";
        public const string FileName = "materials.json";
        public const int Format = 1;

        private const string SkinsFolder = Root + "/skins";
        private const string SpeciesFolder = Root + "/species";
        private const string VanillaFolder = Root + "/vanilla-skins";

        /// <summary>The owner's folder, relative to the mod folder, with '/' separators.</summary>
        public static string FolderOf(MaterialOwner owner) => owner.Kind switch
        {
            MaterialOwnerKind.Skin => SkinsFolder + "/" + owner.SkinId,
            MaterialOwnerKind.Species => SpeciesFolder + "/" + Sanitize(owner.Species ?? ""),
            _ => VanillaFolder + "/" + Sanitize(owner.Species + "-" + owner.SkinName),
        };

        /// <summary>True for a file in the mod's own materials/ folder (an image saved in game may be newer than the mod's cooked copy).</summary>
        public static bool IsMaterialImage(string path, string modDir) =>
            ModPaths.IsInside(path, Path.Combine(modDir, Root));

        /// <summary>A name as one path segment: characters a file name cannot hold (and '/') become '_', then trailing '.' and spaces go.</summary>
        public static string Sanitize(string segment)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(segment.Select(c => c == '/' || invalid.Contains(c) ? '_' : c).ToArray()).TrimEnd('.', ' ');
            return cleaned.Length > 0 ? cleaned : "_";
        }

        /// <summary>Reads every materials.json into the manifest; a bad file adds one problem and is skipped, the rest still load.</summary>
        public static void Load(ModManifest manifest, string modDir, List<string> problems)
        {
            var loaded = Loaded.GetOrCreateValue(manifest);
            loaded.Clear();
            manifest.SpeciesMaterials.Clear();
            manifest.VanillaSkinMaterials.Clear();
            foreach (var skin in manifest.Skins) skin.Materials = null;

            foreach (var folder in Folders(modDir, SkinsFolder))
            {
                var skin = manifest.Skins.FirstOrDefault(s => s.Id == folder.Name);
                if (skin == null) continue; // a folder for a skin this mod does not have
                Read(folder, problems, loaded, map => skin.Materials = Materials(map, $"Skin \"{skin.Id}\"", true));
            }
            foreach (var folder in Folders(modDir, SpeciesFolder))
                Read(folder, problems, loaded, map =>
                {
                    var species = Identity(map, "species", "a \"species\"");
                    if (manifest.SpeciesMaterials.ContainsKey(species)) throw new ManifestException($"Species \"{species}\" is already defined by another folder.");
                    if (Materials(map, $"Species \"{species}\"", false) is { } materials) manifest.SpeciesMaterials[species] = materials;
                });
            foreach (var folder in Folders(modDir, VanillaFolder))
                Read(folder, problems, loaded, map =>
                {
                    var species = Identity(map, "species", "a \"species\"");
                    var skin = Identity(map, "skin", "a \"skin\" (the name of the vanilla skin)");
                    if (manifest.VanillaSkinMaterials.ContainsKey(species + "/" + skin))
                        throw new ManifestException($"Vanilla skin \"{species}/{skin}\" is already defined by another folder.");
                    if (Materials(map, $"Vanilla skin \"{species}/{skin}\"", true) is { } materials) manifest.VanillaSkinMaterials[species + "/" + skin] = materials;
                });
        }

        /// <summary>
        /// Writes one materials.json per owner that has materials, deletes the ones whose owner has none (and folders left empty).
        /// Images are never touched.
        /// </summary>
        public static void Save(ModManifest manifest, string modDir)
        {
            var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var loaded = Loaded.GetOrCreateValue(manifest);
            foreach (var (owner, materials, folder) in Owners(manifest))
            {
                kept.Add(folder);
                loaded.Add(folder);
                var map = new Dictionary<string, object?> { ["format"] = Format };
                if (owner.Kind != MaterialOwnerKind.Skin) map["species"] = owner.Species;
                if (owner.Kind == MaterialOwnerKind.VanillaSkin) map["skin"] = owner.SkinName;
                map["materials"] = MaterialOverrides.ToJson(materials);
                var directory = Path.Combine(modDir, folder.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(directory);
                AtomicFile.Write(Path.Combine(directory, FileName), Json.Write(map) + "\n");
            }
            foreach (var root in new[] { SkinsFolder, SpeciesFolder, VanillaFolder })
            {
                foreach (var folder in Folders(modDir, root))
                {
                    if (kept.Contains(folder.Relative) || !loaded.Contains(folder.Relative)) continue; // only what Load read or Save wrote
                    var file = Path.Combine(folder.Directory, FileName);
                    if (File.Exists(file)) File.Delete(file);
                    DeleteIfEmpty(folder.Directory);
                }
                DeleteIfEmpty(Path.Combine(modDir, root.Replace('/', Path.DirectorySeparatorChar)));
            }
            DeleteIfEmpty(Path.Combine(modDir, Root));
        }

        /// <summary>
        /// Read-modify-write of ONE owner's materials.json: the given materials replace those of the same names (an empty block
        /// removes it), the file's other materials stay, and no other owner's file is touched. Null or an empty map removes the
        /// owner's file (and folders left empty). Throws <see cref="ManifestException"/> when the existing file cannot be read.
        /// </summary>
        public static void SaveOwner(string modDir, MaterialOwner owner, Dictionary<string, MaterialOverride>? materials)
        {
            var folder = ResolveFolder(modDir, owner);
            var directory = Path.Combine(modDir, folder.Replace('/', Path.DirectorySeparatorChar));
            var file = Path.Combine(directory, FileName);
            var merged = new Dictionary<string, MaterialOverride>(StringComparer.Ordinal);
            if (materials != null && materials.Count > 0)
            {
                if (File.Exists(file) && ReadMap(file) is { } existing)
                    foreach (var pair in MaterialOverrides.Parse(existing.TryGetValue("materials", out var m) ? m : null, folder + "/" + FileName, owner.Kind != MaterialOwnerKind.Species) ?? new Dictionary<string, MaterialOverride>())
                        merged[pair.Key] = pair.Value;
                foreach (var pair in materials)
                    if (pair.Value.IsEmpty) merged.Remove(pair.Key);
                    else merged[pair.Key] = pair.Value;
            }
            if (merged.Count == 0)
            {
                if (File.Exists(file)) File.Delete(file);
                DeleteIfEmpty(directory);
                foreach (var root in new[] { SkinsFolder, SpeciesFolder, VanillaFolder }) DeleteIfEmpty(Path.Combine(modDir, root.Replace('/', Path.DirectorySeparatorChar)));
                DeleteIfEmpty(Path.Combine(modDir, Root));
                return;
            }
            var map = new Dictionary<string, object?> { ["format"] = Format };
            if (owner.Kind != MaterialOwnerKind.Skin) map["species"] = owner.Species;
            if (owner.Kind == MaterialOwnerKind.VanillaSkin) map["skin"] = owner.SkinName;
            map["materials"] = MaterialOverrides.ToJson(merged);
            Directory.CreateDirectory(directory);
            AtomicFile.Write(file, Json.Write(map) + "\n");
        }

        // The owner's own folder: for species and vanilla skins one already holding its identity (names that sanitize alike share
        // "-2", "-3" folders), else the first free name, like Save.
        private static string ResolveFolder(string modDir, MaterialOwner owner)
        {
            var folder = FolderOf(owner);
            if (owner.Kind == MaterialOwnerKind.Skin) return folder;
            for (var n = 1; ; n++)
            {
                var candidate = n == 1 ? folder : folder + "-" + n;
                var file = Path.Combine(modDir, candidate.Replace('/', Path.DirectorySeparatorChar), FileName);
                if (!File.Exists(file)) return candidate;
                var map = ReadMap(file);
                if (map != null && map.TryGetValue("species", out var species) && species as string == owner.Species
                    && (owner.Kind == MaterialOwnerKind.Species || (map.TryGetValue("skin", out var skin) && skin as string == owner.SkinName)))
                    return candidate;
            }
        }

        private static Dictionary<string, object?>? ReadMap(string file)
        {
            try
            {
                return Json.Parse(File.ReadAllText(file)) as Dictionary<string, object?>;
            }
            catch (FormatException ex)
            {
                throw new ManifestException($"\"{file}\" is not valid JSON: {ex.Message}");
            }
        }

        /// <summary>Mod-relative paths of every materials.json the manifest has, and of every image its blocks name.</summary>
        public static IEnumerable<string> Files(ModManifest manifest)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, materials, folder) in Owners(manifest))
            {
                var file = folder + "/" + FileName;
                if (seen.Add(file)) yield return file;
                foreach (var image in materials.Values.SelectMany(Images))
                {
                    var path = image.Replace('\\', '/');
                    if (seen.Add(path)) yield return path;
                }
            }
        }

        private static IEnumerable<string> Images(MaterialOverride material)
        {
            if (material.Iridescence?.Ramp?.File is string ramp) yield return ramp;
            if (material.Iridescence?.Mask?.File is string mask) yield return mask;
            if (material.Fur?.Mask?.File is string fur) yield return fur;
            if (material.Fur?.InfantMask?.File is string infant) yield return infant;
        }

        // The folders each manifest's Load read (or Save wrote): Save deletes only those, never a file it did not understand.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ModManifest, HashSet<string>> Loaded =
            new System.Runtime.CompilerServices.ConditionalWeakTable<ModManifest, HashSet<string>>();

        /// <summary>Every owner with materials and its folder; names that sanitize alike get "-2", "-3" (the identity is in the file).</summary>
        private static IEnumerable<(MaterialOwner Owner, Dictionary<string, MaterialOverride> Materials, string Folder)> Owners(ModManifest manifest)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string Unique(MaterialOwner owner)
            {
                var folder = FolderOf(owner);
                var candidate = folder;
                for (var n = 2; !used.Add(candidate); n++) candidate = folder + "-" + n;
                return candidate;
            }
            foreach (var skin in manifest.Skins)
                if (skin.Materials is { Count: > 0 } skinMaterials)
                {
                    var owner = new MaterialOwner(MaterialOwnerKind.Skin, skin.Id, null, null);
                    yield return (owner, skinMaterials, Unique(owner));
                }
            foreach (var pair in manifest.SpeciesMaterials.OrderBy(p => p.Key, StringComparer.Ordinal))
                if (pair.Value.Count > 0)
                {
                    var owner = new MaterialOwner(MaterialOwnerKind.Species, null, pair.Key, null);
                    yield return (owner, pair.Value, Unique(owner));
                }
            foreach (var pair in manifest.VanillaSkinMaterials.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (pair.Value.Count == 0) continue;
                var slash = pair.Key.IndexOf('/');
                if (slash <= 0 || slash == pair.Key.Length - 1)
                    throw new ManifestException($"Vanilla skin materials \"{pair.Key}\": the key must be \"<species>/<skin>\".");
                var owner = new MaterialOwner(MaterialOwnerKind.VanillaSkin, null, pair.Key.Substring(0, slash), pair.Key.Substring(slash + 1));
                yield return (owner, pair.Value, Unique(owner));
            }
        }

        private sealed class OwnerFolder
        {
            public string Name { get; set; } = "";
            public string Relative { get; set; } = "";
            public string Directory { get; set; } = "";
        }

        private static IEnumerable<OwnerFolder> Folders(string modDir, string root)
        {
            var directory = Path.Combine(modDir, root.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory)) yield break;
            foreach (var path in Directory.GetDirectories(directory).OrderBy(p => p, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(path);
                yield return new OwnerFolder { Name = name, Relative = root + "/" + name, Directory = path };
            }
        }

        private static void DeleteIfEmpty(string directory)
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }

        private static void Read(OwnerFolder folder, List<string> problems, HashSet<string> loaded, Action<Dictionary<string, object?>> apply)
        {
            var file = Path.Combine(folder.Directory, FileName);
            if (!File.Exists(file)) return;
            var label = folder.Relative + "/" + FileName;
            try
            {
                object? root;
                try
                {
                    root = Json.Parse(File.ReadAllText(file));
                }
                catch (FormatException ex)
                {
                    throw new ManifestException("It is not valid JSON: " + ex.Message);
                }
                if (!(root is Dictionary<string, object?> map)) throw new ManifestException("It must be a JSON object.");
                if (!(map.TryGetValue("format", out var format) && format is double f && f == Format)) throw new ManifestException($"\"format\" must be {Format}.");
                apply(map);
                loaded.Add(folder.Relative);
            }
            catch (ManifestException ex)
            {
                problems.Add($"{label}: {ex.Message}");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                problems.Add($"{label}: could not be read ({ex.Message})");
            }
        }

        private static string Identity(Dictionary<string, object?> map, string key, string what) =>
            map.TryGetValue(key, out var value) && value is string text && text.Trim().Length > 0 ? text.Trim() : throw new ManifestException($"It needs {what}.");

        private static Dictionary<string, MaterialOverride>? Materials(Dictionary<string, object?> map, string owner, bool skinLevel) =>
            map.TryGetValue("materials", out var materials) && materials != null ? MaterialOverrides.Parse(materials, owner, skinLevel) : null;
    }
}
