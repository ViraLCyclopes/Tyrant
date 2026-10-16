using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tyrant.Framework.Core
{
    public static class FrameworkInfo
    {
        /// <summary>The framework version mods compare their "requires": { "tyrant": ... } against.</summary>
        public const string Version = "0.2.0";
    }

    public static class ModId
    {
        /// <summary>3–64 characters of a-z, 0-9 and '-', not starting or ending with '-'; also the mod's folder name.</summary>
        public static bool IsValid(string? id) =>
            id != null && id.Length >= 3 && id.Length <= 64 && id[0] != '-' && id[id.Length - 1] != '-'
            && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    }

    public sealed class ManifestException : Exception
    {
        public ManifestException(string message) : base(message) { }
    }

    /// <summary>One "replace" entry: swap the game texture named Texture for the mod's PNG at File.</summary>
    public sealed class TextureReplacement
    {
        /// <summary>The texture's Unity object name; what the framework matches on.</summary>
        public string Texture { get; set; } = "";

        /// <summary>Addressables key (container path), recorded for an Addressables-level hook later.</summary>
        public string? Key { get; set; }

        /// <summary>Addressables GUID, recorded for an Addressables-level hook later.</summary>
        public string? Guid { get; set; }

        /// <summary>The PNG, relative to the mod folder.</summary>
        public string File { get; set; } = "";
    }

    /// <summary>The texture slots a skin can provide, as written in mod.json, with their shader properties.</summary>
    /// <summary>
    /// A species' model replacement and/or rig edit: Target is a species id (or, later, an object name); File is the user's
    /// .glb in the mod, "" for a rig edit on the game's own mesh.
    /// </summary>
    public sealed class ModelReplacement
    {
        public string Target { get; set; } = "";

        /// <summary>The prefab Tyrant matched (Addressables path), for Check and the app.</summary>
        public string? Key { get; set; }

        public string File { get; set; } = "";

        /// <summary>The rig edit (bone → offset), null for none; the model's bind poses are built for it.</summary>
        public Dictionary<string, RigOffset>? Rig { get; set; }

        /// <summary>How the rig edit changes the species' head-look boom (Tyrant measures it from the skeleton); null for none.</summary>
        public HeadRigCorrection? HeadRig { get; set; }
    }

    /// <summary>
    /// A sound replacement: the game's FMOD event plays one of Files instead — for everyone, or only for a species or a skin
    /// (Species / Skin; a skin key is "&lt;mod&gt;/&lt;skin&gt;" or a vanilla "&lt;species&gt;/&lt;skinName&gt;").
    /// </summary>
    public sealed class SoundReplacement
    {
        public string Event { get; set; } = "";
        public string? Species { get; set; }
        public string? Skin { get; set; }

        /// <summary>Audio files in the mod (WAV, OGG, MP3, FLAC); several = one picked at random each time.</summary>
        public List<string> Files { get; } = new List<string>();

        /// <summary>0–2.</summary>
        public double Volume { get; set; } = 1.0;

        /// <summary>0–1: how much babies play higher (animal sounds only).</summary>
        public double AgePitch { get; set; } = 1.0;

        /// <summary>0–1: how often a one-off sound plays when the game starts it; null follows the game's own chances.</summary>
        public double? Chance { get; set; }

        public bool IsUnique => Species != null || Skin != null;
    }

    public static class SkinSlotNames
    {
        public static readonly IReadOnlyList<string> All = new[]
        {
            "diffuse", "normal", "extra", "pattern", "fur", "infantDiffuse", "infantNormal", "infantExtra", "infantPattern", "infantFur",
        };

        public static string? Canonical(string slot) => All.FirstOrDefault(s => string.Equals(s, slot, StringComparison.OrdinalIgnoreCase));

        public static string Property(string slot)
        {
            var infant = slot.StartsWith("infant", StringComparison.Ordinal);
            var part = infant ? slot.Substring("infant".Length).ToLowerInvariant() : slot;
            var name = part switch
            {
                "diffuse" => "Diffuse",
                "normal" => "Normal",
                "extra" => "ExtraMap",
                "pattern" => "PatternMask",
                "fur" => "FurMask",
                _ => throw new ArgumentException("Unknown skin slot " + slot),
            };
            return (infant ? "_Infant" : "_Adult") + name;
        }

        public static SlotKind KindOf(string slot) =>
            slot.EndsWith("iffuse", StringComparison.OrdinalIgnoreCase) ? SlotKind.Color
            : slot.EndsWith("ormal", StringComparison.OrdinalIgnoreCase) ? SlotKind.Normal
            : SlotKind.Data;
    }

    /// <summary>One added skin: a copy of a vanilla skin (Base) with the mod's PNGs per sex and slot.</summary>
    public sealed class SkinEntry
    {
        public string Id { get; set; } = "";
        public string Species { get; set; } = "";
        public string Name { get; set; } = "";

        /// <summary>The vanilla skin to copy: its skinName, or its 0-based index as text.</summary>
        public string Base { get; set; } = "0";

        public string? Thumbnail { get; set; }

        /// <summary>Portrait slot (PortraitSlots.All) → PNG in the mod; a slot left out shows the base skin's portrait.</summary>
        public Dictionary<string, string>? Portraits { get; set; }

        /// <summary>Slot → PNG (relative to the mod folder); null when the skin has no male textures.</summary>
        public Dictionary<string, string>? Male { get; set; }

        public Dictionary<string, string>? Female { get; set; }

        /// <summary>The skin's own colours (mod.json "colors"); null keeps the base skin's.</summary>
        public SkinColors? Colors { get; set; }

        /// <summary>The skin's own model (a .glb in the mod), overriding the species' model.</summary>
        public string? Model { get; set; }

        /// <summary>The skin's rig edit (bone → offset), null for none; with it the skin overrides the species' model and rig.</summary>
        public Dictionary<string, RigOffset>? Rig { get; set; }

        /// <summary>Tyrant's own grouping of a mod's skins in the mod editor ("Jurassic Park"); the game ignores it.</summary>
        public string? Group { get; set; }

        /// <summary>
        /// mod.json "starting": this skin is its custom species' starting skin (the species' skin 0, no number of its own). Only
        /// on this mod's own species, one per species, with base "0" (the plain template clone); see <see cref="StartingSkins"/>.
        /// </summary>
        public bool Starting { get; set; }

        /// <summary>mod.json "copiedFrom": the game skin of another species its PNGs were copied from (Edit a copy reads it).</summary>
        public SkinSource? CopiedFrom { get; set; }

        public const int MaxGroupLength = 64;

        /// <summary>A group name as saved: trimmed and at most 64 characters; empty is no group.</summary>
        public static string? GroupName(string? name)
        {
            var trimmed = name?.Trim();
            if (string.IsNullOrEmpty(trimmed)) return null;
            return trimmed!.Length > MaxGroupLength ? trimmed.Substring(0, MaxGroupLength) : trimmed;
        }

        /// <summary>The skin's permanent key in skin-slots.json.</summary>
        public string Key(string modId) => modId + "/" + Id;
    }

    /// <summary>A game skin of a species, by its 0-based number.</summary>
    public sealed class SkinSource
    {
        public string Species { get; set; } = "";
        public int Skin { get; set; }
    }

    /// <summary>mod.json "textures": how Tyrant compresses the mod's textures when it cooks them (install, export).</summary>
    public sealed class TextureSettings
    {
        public TextureCompression Compression { get; set; } = TextureCompression.Auto;

        /// <summary>File (relative to the mod, as mod.json names it) → its own compression.</summary>
        public Dictionary<string, TextureCompression> Overrides { get; } = new Dictionary<string, TextureCompression>(StringComparer.OrdinalIgnoreCase);

        public TextureCompression For(string file) => Overrides.TryGetValue(file.Replace('\\', '/'), out var mode) ? mode : Compression;

        internal bool IsDefault => Compression == TextureCompression.Auto && Overrides.Count == 0;
    }

    /// <summary>A mod's mod.json: who it is, what it needs, and what each content module should do.</summary>
    public sealed class ModManifest
    {
        public const string FileName = "mod.json";
        public const int CurrentFormat = 1;

        public int Format { get; set; } = CurrentFormat;
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "1.0.0";
        public string? Author { get; set; }
        public string? Description { get; set; }

        /// <summary>The lowest framework version this mod works with, e.g. "0.1" or ">=0.1".</summary>
        public string? RequiresTyrant { get; set; }

        public List<string> Dependencies { get; } = new List<string>();

        /// <summary>A code mod's DLL, relative to the mod folder.</summary>
        public string? Assembly { get; set; }

        public List<TextureReplacement> Replace { get; } = new List<TextureReplacement>();

        public List<SkinEntry> Skins { get; } = new List<SkinEntry>();

        /// <summary>In-memory species from animals.db; never serialized into mod.json.</summary>
        public List<SpeciesEntry> Species { get; } = new List<SpeciesEntry>();

        public List<ModelReplacement> Models { get; } = new List<ModelReplacement>();

        public List<SoundReplacement> Sounds { get; } = new List<SoundReplacement>();

        /// <summary>mod.json "textures"; never null (absent = auto everywhere).</summary>
        public TextureSettings Textures { get; } = new TextureSettings();

        public static ModManifest Parse(string json) => Parse(json, null);

        /// <summary>
        /// With <paramref name="skippedSkins"/> (the game), a bad "skins" entry is dropped with a message there and the rest of the
        /// mod still loads; without it (Tyrant's Check), any problem is an error.
        /// </summary>
        public static ModManifest Parse(string json, List<string>? skippedSkins)
        {
            object? root;
            try
            {
                root = Json.Parse(json);
            }
            catch (FormatException ex)
            {
                throw new ManifestException("mod.json is not valid JSON: " + ex.Message);
            }
            if (!(root is Dictionary<string, object?> map)) throw new ManifestException("mod.json must be a JSON object.");

            var format = Integer(map, "format") ?? throw new ManifestException("mod.json has no \"format\" (use 1).");
            if (format > CurrentFormat)
                throw new ManifestException($"mod.json format {format} needs a newer Tyrant framework (this one reads format {CurrentFormat}).");
            if (format < 1) throw new ManifestException($"mod.json format {format} is not valid (use 1).");

            var id = Text(map, "id") ?? "";
            if (!ModId.IsValid(id))
                throw new ManifestException($"\"id\" must be 3–64 lowercase letters, digits or '-' (got \"{id}\").");

            var manifest = new ModManifest
            {
                Format = format,
                Id = id,
                Name = Text(map, "name") is { Length: > 0 } name ? name : id,
                Version = Text(map, "version") is { Length: > 0 } version ? version : "1.0.0",
                Author = Text(map, "author"),
                Description = Text(map, "description"),
                Assembly = Text(map, "assembly"),
                RequiresTyrant = map.TryGetValue("requires", out var requires) && requires is Dictionary<string, object?> r ? Text(r, "tyrant") : null,
            };

            foreach (var dependency in Array(map, "dependencies"))
                manifest.Dependencies.Add(dependency as string ?? throw new ManifestException("\"dependencies\" must list mod ids as strings."));

            var n = 0;
            foreach (var item in Array(map, "replace"))
            {
                n++;
                if (!(item is Dictionary<string, object?> entry)) throw new ManifestException($"\"replace\" entry {n} must be an object.");
                var texture = Text(entry, "texture");
                if (string.IsNullOrWhiteSpace(texture)) throw new ManifestException($"\"replace\" entry {n} has no \"texture\".");
                var file = Text(entry, "file");
                if (string.IsNullOrWhiteSpace(file)) throw new ManifestException($"\"replace\" entry {n} ({texture}) has no \"file\".");
                manifest.Replace.Add(new TextureReplacement { Texture = texture!, File = file!, Key = Text(entry, "key"), Guid = Text(entry, "guid") });
            }
            n = 0;
            foreach (var item in Array(map, "models"))
            {
                n++;
                if (!(item is Dictionary<string, object?> entry)) throw new ManifestException($"\"models\" entry {n} must be an object.");
                var target = Text(entry, "target");
                if (string.IsNullOrWhiteSpace(target)) throw new ManifestException($"\"models\" entry {n} has no \"target\".");
                var file = Text(entry, "file");
                var rig = entry.TryGetValue("rig", out var rigJson) && rigJson != null ? RigEdit.Parse(rigJson, $"\"models\" entry {n} ({target})") : null;
                if (string.IsNullOrWhiteSpace(file) && (rig == null || rig.Count == 0))
                    throw new ManifestException($"\"models\" entry {n} ({target}) has no \"file\" or \"rig\".");
                var headRig = entry.TryGetValue("headRig", out var headJson) && headJson != null ? HeadRigCorrection.Parse(headJson, $"\"models\" entry {n} ({target})") : null;
                manifest.Models.Add(new ModelReplacement { Target = target!, Key = Text(entry, "key"), File = file ?? "", Rig = rig is { Count: > 0 } ? rig : null, HeadRig = headRig });
            }
            n = 0;
            foreach (var item in Array(map, "sounds"))
            {
                n++;
                try
                {
                    manifest.Sounds.Add(ParseSound(item, n));
                }
                catch (ManifestException ex) when (skippedSkins != null)
                {
                    skippedSkins.Add($"\"sounds\" entry {n} was skipped: {ex.Message}");
                }
            }
            n = 0;
            foreach (var item in Array(map, "skins"))
            {
                n++;
                try
                {
                    ParseSkin(item, n, manifest);
                }
                catch (ManifestException ex) when (skippedSkins != null)
                {
                    skippedSkins.Add($"\"skins\" entry {n} was skipped: {ex.Message}");
                }
            }
            try
            {
                ParseTextures(map, manifest.Textures);
            }
            catch (ManifestException ex) when (skippedSkins != null)
            {
                // The game ignores it: compression only matters to Tyrant when it cooks.
                manifest.Textures.Compression = TextureCompression.Auto;
                manifest.Textures.Overrides.Clear();
                skippedSkins.Add("\"textures\" was ignored: " + ex.Message);
            }
            if (map.ContainsKey("species")) throw new ManifestException("Species definitions belong in animals.db, not mod.json.");
            return manifest;
        }

        private static void ParseSkin(object? item, int n, ModManifest manifest)
        {
            {
                if (!(item is Dictionary<string, object?> entry)) throw new ManifestException($"\"skins\" entry {n} must be an object.");
                var skinId = Text(entry, "id") ?? "";
                if (!ModId.IsValid(skinId))
                    throw new ManifestException($"\"skins\" entry {n}: \"id\" must be 3–64 lowercase letters, digits or '-' (got \"{skinId}\").");
                if (manifest.Skins.Any(s => s.Id == skinId)) throw new ManifestException($"Two skins have the id \"{skinId}\".");
                var species = Text(entry, "species");
                if (string.IsNullOrWhiteSpace(species)) throw new ManifestException($"Skin \"{skinId}\" has no \"species\".");
                var skin = new SkinEntry
                {
                    Id = skinId,
                    Species = species!,
                    Name = Text(entry, "name") is { Length: > 0 } skinName ? skinName : skinId,
                    Base = Text(entry, "base") is { Length: > 0 } baseSkin ? baseSkin : "0",
                    Thumbnail = Text(entry, "thumbnail"),
                    Portraits = PortraitMap(entry, skinId),
                    Male = Slots(entry, "male", skinId),
                    Female = Slots(entry, "female", skinId),
                    Colors = entry.TryGetValue("colors", out var colors) && colors != null ? SkinColors.Parse(colors, skinId) : null,
                    Model = Text(entry, "model"),
                    Rig = entry.TryGetValue("rig", out var rig) && rig != null && RigEdit.Parse(rig, $"Skin \"{skinId}\"") is { Count: > 0 } parsed ? parsed : null,
                    Group = SkinEntry.GroupName(Text(entry, "group")),
                    Starting = entry.TryGetValue("starting", out var starting) && starting != null
                        && (starting as bool? ?? throw new ManifestException($"Skin \"{skinId}\": \"starting\" must be true or false.")),
                    CopiedFrom = entry.TryGetValue("copiedFrom", out var copied) && copied != null ? Source(copied, skinId) : null,
                };
                if ((skin.Male?.Count ?? 0) == 0 && (skin.Female?.Count ?? 0) == 0)
                    throw new ManifestException($"Skin \"{skinId}\" needs \"male\" or \"female\" textures.");
                manifest.Skins.Add(skin);
            }
        }

        private static SkinSource Source(object value, string skinId)
        {
            var map = value as Dictionary<string, object?>;
            var species = map == null ? null : Text(map, "species");
            var skin = map == null ? null : Integer(map, "skin");
            if (string.IsNullOrWhiteSpace(species) || !(skin >= 0))
                throw new ManifestException($"Skin \"{skinId}\": \"copiedFrom\" needs a \"species\" and a \"skin\" number (0 or more).");
            return new SkinSource { Species = species!, Skin = skin!.Value };
        }

        public string ToJson()
        {
            var map = new Dictionary<string, object?> { ["format"] = Format, ["id"] = Id, ["name"] = Name, ["version"] = Version };
            if (Author != null) map["author"] = Author;
            if (Description != null) map["description"] = Description;
            if (RequiresTyrant != null) map["requires"] = new Dictionary<string, object?> { ["tyrant"] = RequiresTyrant };
            if (Dependencies.Count > 0) map["dependencies"] = Dependencies.Cast<object?>().ToList();
            if (Assembly != null) map["assembly"] = Assembly;
            map["replace"] = Replace.Select(r =>
            {
                var entry = new Dictionary<string, object?> { ["texture"] = r.Texture };
                if (r.Key != null) entry["key"] = r.Key;
                if (r.Guid != null) entry["guid"] = r.Guid;
                entry["file"] = r.File;
                return (object?)entry;
            }).ToList();
            if (Models.Count > 0)
                map["models"] = Models.Select(m =>
                {
                    var entry = new Dictionary<string, object?> { ["target"] = m.Target };
                    if (m.Key != null) entry["key"] = m.Key;
                    if (m.File.Length > 0) entry["file"] = m.File;
                    if (m.Rig is { Count: > 0 }) entry["rig"] = RigEdit.ToJson(m.Rig);
                    if (m.HeadRig != null) entry["headRig"] = m.HeadRig.ToMap();
                    return (object?)entry;
                }).ToList();
            if (Sounds.Count > 0)
                map["sounds"] = Sounds.Select(s =>
                {
                    var entry = new Dictionary<string, object?> { ["event"] = s.Event };
                    if (s.Species != null) entry["species"] = s.Species;
                    if (s.Skin != null) entry["skin"] = s.Skin;
                    entry["files"] = s.Files.Cast<object?>().ToList();
                    if (s.Volume != 1.0) entry["volume"] = s.Volume;
                    if (s.AgePitch != 1.0) entry["agePitch"] = s.AgePitch;
                    if (s.Chance is double chance) entry["chance"] = chance;
                    return (object?)entry;
                }).ToList();
            if (Skins.Count > 0)
                map["skins"] = Skins.Select(s =>
                {
                    var entry = new Dictionary<string, object?> { ["id"] = s.Id, ["species"] = s.Species, ["name"] = s.Name, ["base"] = s.Base };
                    if (s.Starting) entry["starting"] = true;
                    if (s.CopiedFrom != null) entry["copiedFrom"] = new Dictionary<string, object?> { ["species"] = s.CopiedFrom.Species, ["skin"] = s.CopiedFrom.Skin };
                    if (s.Group != null) entry["group"] = s.Group;
                    if (s.Thumbnail != null) entry["thumbnail"] = s.Thumbnail;
                    if (s.Portraits is { Count: > 0 }) entry["portraits"] = PortraitSlots.All.Where(s.Portraits.ContainsKey).ToDictionary(k => k, k => (object?)s.Portraits[k]);
                    if (s.Male != null) entry["male"] = Ordered(s.Male);
                    if (s.Female != null) entry["female"] = Ordered(s.Female);
                    if (s.Model != null) entry["model"] = s.Model;
                    if (s.Rig is { Count: > 0 }) entry["rig"] = RigEdit.ToJson(s.Rig);
                    if (s.Colors != null) entry["colors"] = s.Colors.ToJson();
                    return (object?)entry;
                }).ToList();
            if (!Textures.IsDefault)
            {
                var block = new Dictionary<string, object?> { ["compression"] = TextureCompressions.Name(Textures.Compression) };
                if (Textures.Overrides.Count > 0)
                    block["overrides"] = Textures.Overrides.OrderBy(o => o.Key, StringComparer.Ordinal)
                        .ToDictionary(o => o.Key, o => (object?)TextureCompressions.Name(o.Value));
                map["textures"] = block;
            }
            return Json.Write(map) + "\n";
        }

        private static void ParseTextures(Dictionary<string, object?> map, TextureSettings settings)
        {
            if (!map.TryGetValue("textures", out var value) || value == null) return;
            if (!(value is Dictionary<string, object?> block)) throw new ManifestException("\"textures\" must be an object with \"compression\" and \"overrides\".");
            if (block.TryGetValue("compression", out var compression) && compression != null)
                settings.Compression = TextureCompressions.Parse(compression as string)
                    ?? throw new ManifestException($"\"textures.compression\" must be \"auto\" or \"bc7\" (got {Json.Write(compression)}).");
            if (!block.TryGetValue("overrides", out var overrides) || overrides == null) return;
            if (!(overrides is Dictionary<string, object?> files)) throw new ManifestException("\"textures.overrides\" must map texture files to \"auto\" or \"bc7\".");
            foreach (var pair in files)
                settings.Overrides[pair.Key.Replace('\\', '/')] = TextureCompressions.Parse(pair.Value as string)
                    ?? throw new ManifestException($"\"textures.overrides\" for \"{pair.Key}\" must be \"auto\" or \"bc7\".");
        }

        private static SoundReplacement ParseSound(object? item, int n)
        {
            if (!(item is Dictionary<string, object?> entry)) throw new ManifestException($"\"sounds\" entry {n} must be an object.");
            var eventPath = Text(entry, "event");
            if (string.IsNullOrWhiteSpace(eventPath)) throw new ManifestException($"\"sounds\" entry {n} has no \"event\".");
            var sound = new SoundReplacement { Event = eventPath!, Species = Text(entry, "species"), Skin = Text(entry, "skin") };
            if (sound.Species != null && sound.Skin != null)
                throw new ManifestException($"Sound {eventPath}: give it a \"species\" or a \"skin\", not both.");
            foreach (var file in Array(entry, "files"))
                sound.Files.Add(file as string ?? throw new ManifestException($"Sound {eventPath}: \"files\" must list file paths."));
            if (sound.Files.Count == 0) throw new ManifestException($"Sound {eventPath} has no files.");
            if (entry.TryGetValue("volume", out var volume) && volume is double v) sound.Volume = Math.Max(0, Math.Min(2, v));
            if (entry.TryGetValue("agePitch", out var agePitch) && agePitch is double a) sound.AgePitch = Math.Max(0, Math.Min(1, a));
            if (entry.TryGetValue("chance", out var chance) && chance is double c) sound.Chance = Math.Max(0, Math.Min(1, c));
            return sound;
        }

        private static string? Text(Dictionary<string, object?> map, string key) =>
            map.TryGetValue(key, out var value) ? value as string : null;

        private static int? Integer(Dictionary<string, object?> map, string key) =>
            map.TryGetValue(key, out var value) && value is double d && d == Math.Floor(d) ? (int)d : (int?)null;

        private static IEnumerable<object?> Array(Dictionary<string, object?> map, string key)
        {
            if (!map.TryGetValue(key, out var value) || value == null) return System.Array.Empty<object?>();
            return value as List<object?> ?? throw new ManifestException($"\"{key}\" must be a list.");
        }

        private static Dictionary<string, string>? PortraitMap(Dictionary<string, object?> entry, string skinId)
        {
            if (!entry.TryGetValue("portraits", out var value) || value == null) return null;
            if (!(value is Dictionary<string, object?> map)) throw new ManifestException($"Skin \"{skinId}\": \"portraits\" must map portrait slots to files.");
            var portraits = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in map)
            {
                if (!PortraitSlots.IsSlot(pair.Key))
                    throw new ManifestException($"Skin \"{skinId}\" uses unknown portrait \"{pair.Key}\" (use {string.Join(", ", PortraitSlots.All)}).");
                portraits[pair.Key] = pair.Value as string ?? throw new ManifestException($"Skin \"{skinId}\": \"portraits.{pair.Key}\" must be a file path.");
            }
            return portraits.Count == 0 ? null : portraits;
        }

        private static Dictionary<string, string>? Slots(Dictionary<string, object?> entry, string sex, string skinId)
        {
            if (!entry.TryGetValue(sex, out var value) || value == null) return null;
            if (!(value is Dictionary<string, object?> map)) throw new ManifestException($"Skin \"{skinId}\": \"{sex}\" must map slots to files.");
            var slots = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in map)
            {
                var slot = SkinSlotNames.Canonical(pair.Key)
                    ?? throw new ManifestException($"Skin \"{skinId}\" uses unknown slot \"{pair.Key}\" (use {string.Join(", ", SkinSlotNames.All)}).");
                slots[slot] = pair.Value as string ?? throw new ManifestException($"Skin \"{skinId}\": \"{sex}.{pair.Key}\" must be a file path.");
            }
            return slots;
        }

        private static Dictionary<string, object?> Ordered(Dictionary<string, string> slots) =>
            SkinSlotNames.All.Where(slots.ContainsKey).ToDictionary(s => s, s => (object?)slots[s]);
    }
}
