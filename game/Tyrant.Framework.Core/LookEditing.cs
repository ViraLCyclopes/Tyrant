using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>Where the iridescence colour comes from, as the in-game editor offers it (colour stops come from the app).</summary>
    public enum RampKind { Game, ThinFilm, Stops, File }

    /// <summary>Where the iridescence shows, as the in-game editor offers it.</summary>
    public enum MaskKind { Everywhere, Game, Channel, File }

    /// <summary>The in-game editor's choices and typed values: what each scope offers, ranges, parsing and the starting look.</summary>
    public static class LookEditing
    {
        /// <summary>The app's starting film thickness (nm), and part A's starting shaping (Microraptor's) that makes a new sheen visible at once.</summary>
        public const float DefaultThickness = 420;
        public const float StartScale = -0.25f, StartPower = -0.3f, StartBias = 1.3f;

        /// <summary>The fur length Grow fur starts with on a skin whose fur has none (the shader's _Length runs 0 to 4).</summary>
        public const float StartFurLength = 0.5f;

        public const float MaxThickness = 2000;
        public const float MinShaping = -10, MaxShaping = 10;

        /// <summary>The file names Export mask PNG writes into the save target's folder.</summary>
        public const string IridescenceMaskFile = "iridescence-mask.png";
        public const string FurMaskFile = "fur-mask.png";

        /// <summary>The skin slots and channels a mask can come from.</summary>
        public static readonly IReadOnlyList<string> Slots = new[] { "extra", "pattern", "fur" };
        public static readonly IReadOnlyList<char> Channels = new[] { 'r', 'g', 'b', 'a' };

        /// <summary>The variants a strength holds, in the game's AnimalSkinType order.</summary>
        public static readonly IReadOnlyList<string> Variants = new[] { "normal", "albino", "melanistic", "leucistic" };

        private static readonly MaskKind[] AllMasks = { MaskKind.Everywhere, MaskKind.Game, MaskKind.Channel, MaskKind.File };
        private static readonly MaskKind[] SpeciesMasks = { MaskKind.Everywhere, MaskKind.Game, MaskKind.File };

        /// <summary>The mask choices of a scope: a species file cannot hold a mask from a skin slot ("from"), so it never offers one.</summary>
        public static IReadOnlyList<MaskKind> MaskKinds(LookScope scope) => scope == LookScope.Species ? SpeciesMasks : AllMasks;

        /// <summary>Game for no ramp and for the explicit "game" one.</summary>
        public static RampKind RampKindOf(RampSource? ramp) =>
            ramp == null || ramp.Game ? RampKind.Game : ramp.File != null ? RampKind.File : ramp.ThinFilm != null ? RampKind.ThinFilm : RampKind.Stops;

        /// <summary>Game for no mask and for the explicit "game" one.</summary>
        public static MaskKind MaskKindOf(MaskSource? mask) =>
            mask == null || mask.Game ? MaskKind.Game : mask.Everywhere ? MaskKind.Everywhere : mask.File != null ? MaskKind.File : MaskKind.Channel;

        /// <summary>A typed number ("0.5" or "0,5"), kept within min..max; false when the text is not a number.</summary>
        public static bool TryParse(string? text, float min, float max, out float value)
        {
            value = 0;
            var trimmed = text?.Trim().Replace(',', '.');
            if (string.IsNullOrEmpty(trimmed) || !float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                || float.IsNaN(parsed) || float.IsInfinity(parsed))
                return false;
            value = Clamp(parsed, min, max);
            return true;
        }

        /// <summary>A value as its typed-value box shows it: at most three decimals.</summary>
        public static string Format(float value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;

        /// <summary>The strength of one variant ("normal", "albino", …): a variant left out uses the normal one.</summary>
        public static float StrengthFor(VariantNumbers strength, string variant)
        {
            var index = Variants.IndexOf(variant);
            return strength.For(index < 0 ? 0 : index);
        }

        /// <summary>
        /// Sets the strength of the animal's variant. When this level holds no strength yet, it starts from the one shown (from a
        /// lower layer), so per-variant values from below are carried, never flattened into one number.
        /// </summary>
        public static void SetStrength(LookDraft draft, VariantNumbers? shown, string variant, float value)
        {
            if (draft.Current.Iridescence?.Strength == null && shown != null)
                (draft.Current.Iridescence ??= new IridescenceSettings()).Strength = Copy(shown);
            draft.SetStrength(variant, value);
        }

        /// <summary>
        /// Sets one component of a vector setting; the others come from <paramref name="shown"/> (what the animal wears now, often
        /// the game's own value). Every component is kept within the setting's range, so a game value beyond it never makes the
        /// look unsaveable.
        /// </summary>
        public static void SetComponent(LookDraft draft, string key, IReadOnlyList<float> shown, int component, float value)
        {
            var property = MaterialProperties.Find(key) ?? throw new ArgumentException($"Unknown setting \"{key}\".", nameof(key));
            var count = property.Components == 3 ? 3 : Math.Max(2, Math.Min(4, shown.Count));
            var vector = new float[count];
            for (var i = 0; i < count; i++) vector[i] = Clamp(i == component ? value : i < shown.Count ? shown[i] : 0, property.Min, property.Max);
            draft.SetSetting(key, MaterialValue.OfVector(vector));
        }

        /// <summary>True when the animal grows Tyrant's fur at this scope: the draft's own choice, else a lower layer's.</summary>
        /// <param name="below">What the layers under the draft's scope set (<see cref="MaterialTable.MergeBelow"/>), or null.</param>
        public static bool GrowsFur(LookDraft draft, MaterialOverride? below) => (draft.Current.Fur?.Grow ?? below?.Fur?.Grow) == true;

        internal static float? LengthOf(MaterialOverride? block) =>
            block != null && block.Settings.TryGetValue("_Length", out var v) && v.Kind == MaterialValueKind.Number ? v.Number : (float?)null;

        internal static VariantNumbers? Copy(VariantNumbers? s) =>
            s == null ? null : new VariantNumbers { Normal = s.Normal, Albino = s.Albino, Melanistic = s.Melanistic, Leucistic = s.Leucistic };

        private static int IndexOf(this IReadOnlyList<string> list, string value)
        {
            for (var i = 0; i < list.Count; i++)
                if (list[i] == value) return i;
            return -1;
        }
    }

    /// <summary>
    /// The editor's iridescence on/off. Off remembers the strengths it zeroed and on puts them back, so per-variant values
    /// survive a toggle (<see cref="LookDraft.SetIridescenceOn"/> alone would fill 1). A fresh on, where neither the game nor
    /// any layer has a ramp or shaping, also starts a thin film and part A's starting shaping, so the sheen shows at once.
    /// </summary>
    public sealed class IridescenceSwitch
    {
        private bool _remembered;
        private VariantNumbers? _strength;

        /// <summary>A new draft (another animal, scope, a reset or a save): the next on starts fresh.</summary>
        public void Forget()
        {
            _remembered = false;
            _strength = null;
        }

        public void Off(LookDraft draft)
        {
            _strength = LookEditing.Copy(draft.Current.Iridescence?.Strength);
            _remembered = true;
            draft.SetIridescenceOn(false);
        }

        /// <param name="effective">What the layers under the draft's scope set (<see cref="MaterialTable.MergeBelow"/>), or null.</param>
        /// <param name="gameHasRamp">True when the animal's own material has an iridescence ramp.</param>
        public void On(LookDraft draft, MaterialOverride? effective, bool gameHasRamp)
        {
            if (_remembered)
            {
                // Back to the state before off; a strength that came from a lower layer (or the game) is handed back to it.
                _remembered = false;
                var iridescence = draft.Current.Iridescence ??= new IridescenceSettings();
                iridescence.Strength = LookEditing.Copy(_strength);
                if (iridescence.IsEmpty) draft.Current.Iridescence = null;
                _strength = null;
                return;
            }
            var below = effective?.Iridescence;
            var own = draft.Current.Iridescence;
            // A "game" ramp on a material without one is no ramp at all: it seeds like none.
            var seed = !gameHasRamp && Unramped(below?.Ramp) && Unramped(own?.Ramp) && !Shaped(below) && !Shaped(own);
            draft.SetIridescenceOn(true);
            if (!seed) return;
            draft.SetThinFilm(LookEditing.DefaultThickness);
            draft.SetShaping(LookEditing.StartScale, LookEditing.StartPower, LookEditing.StartBias);
        }

        private static bool Unramped(RampSource? ramp) => ramp == null || ramp.Game;

        private static bool Shaped(IridescenceSettings? i) => i != null && (i.Scale != null || i.Power != null || i.Bias != null);
    }

    /// <summary>
    /// The editor's Grow fur on/off. On where nothing gives the fur any length (the draft, the layers below and the game all 0
    /// or unset) also sets <see cref="LookEditing.StartFurLength"/>, so the fur shows at once; off takes that length back while
    /// it is still the one this switch set. Off writes false only over a lower layer that grows fur; otherwise the key goes
    /// (false never takes away fur the game has).
    /// </summary>
    public sealed class FurGrowSwitch
    {
        private bool _seeded;

        /// <summary>A new draft (another animal, scope, a reset or a save): a length seeded before is no longer this switch's.</summary>
        public void Forget() => _seeded = false;

        /// <param name="below">What the layers under the draft's scope set (<see cref="MaterialTable.MergeBelow"/>), or null.</param>
        /// <param name="gameLength">The animal's own _Length (its property block, else its material).</param>
        public void Set(LookDraft draft, MaterialOverride? below, float gameLength, bool on)
        {
            if (!on)
            {
                draft.SetFurGrow(below?.Fur?.Grow == true ? false : (bool?)null);
                if (_seeded && LookEditing.LengthOf(draft.Current) == LookEditing.StartFurLength) draft.SetSetting("_Length", null);
                _seeded = false;
                return;
            }
            draft.SetFurGrow(true);
            if ((LookEditing.LengthOf(draft.Current) ?? LookEditing.LengthOf(below) ?? gameLength) > 0) return;
            draft.SetSetting("_Length", MaterialValue.OfNumber(LookEditing.StartFurLength));
            _seeded = true;
        }
    }

    /// <summary>The image files of an edited look: how they are stored for each save target, and the PNGs the editor offers.</summary>
    public static class LookFiles
    {
        /// <summary>Every file the block names (ramp, mask, fur masks).</summary>
        public static IEnumerable<string> Files(MaterialOverride block)
        {
            if (block.Iridescence?.Ramp?.File is string ramp) yield return ramp;
            if (block.Iridescence?.Mask?.File is string mask) yield return mask;
            if (block.Fur?.Mask?.File is string fur) yield return fur;
            if (block.Fur?.InfantMask?.File is string infant) yield return infant;
        }

        /// <summary>A copy whose files inside <paramref name="baseDir"/> are relative to it ('/' separators); others stay as they are.</summary>
        public static MaterialOverride Relative(MaterialOverride block, string baseDir) =>
            MaterialTable.WithPaths(block, file => ModPaths.IsInside(file, baseDir) ? RelativePath(file, baseDir) : file);

        /// <summary>
        /// A copy for a mod's materials.json: files are mod-relative, and a file outside the mod is copied into
        /// <paramref name="ownerFolder"/> (mod-relative, '/' separators) first: a mod only uses its own files. A file of the same
        /// name and other content is kept, the copy gets "-2", "-3"; the same content already there is reused. A missing outside
        /// file is dropped from the copy and added to <paramref name="missing"/>.
        /// </summary>
        public static MaterialOverride Adopt(MaterialOverride block, string modDir, string ownerFolder, ICollection<string>? missing = null)
        {
            var folder = Path.Combine(modDir, ownerFolder.Replace('/', Path.DirectorySeparatorChar));
            return MaterialTable.WithPaths(block, file =>
            {
                if (ModPaths.IsInside(file, modDir)) return RelativePath(file, modDir);
                if (!File.Exists(file))
                {
                    missing?.Add(file); // a missing file outside the mod cannot be carried in
                    return null;
                }
                Directory.CreateDirectory(folder);
                var bytes = File.ReadAllBytes(file);
                var name = Path.GetFileNameWithoutExtension(file);
                var extension = Path.GetExtension(file);
                for (var n = 1; ; n++)
                {
                    var target = Path.Combine(folder, (n == 1 ? name : name + "-" + n) + extension);
                    if (File.Exists(target) && !File.ReadAllBytes(target).SequenceEqual(bytes)) continue;
                    if (!File.Exists(target)) File.Copy(file, target);
                    return RelativePath(target, modDir);
                }
            });
        }

        /// <summary>The PNGs in these folders (missing ones skipped), each once, sorted by file name.</summary>
        public static IReadOnlyList<string> Pngs(IEnumerable<string> folders)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new List<string>();
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.GetFiles(folder))
                    if (string.Equals(Path.GetExtension(file), ".png", StringComparison.OrdinalIgnoreCase) && seen.Add(Path.GetFullPath(file))) files.Add(file);
            }
            return files.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ThenBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static string Full(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return path;
            }
        }

        private static string RelativePath(string file, string baseDir)
        {
            var root = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(file).Substring(root.Length).Replace('\\', '/');
        }
    }

    /// <summary>
    /// Exported masks waiting for Save: each is written under a pending folder (outside every mod, so no picker lists it and no
    /// share carries it) and the draft points there for the preview. Save moves the ones the saved block uses over their fixed
    /// names (iridescence-mask.png, fur-mask.png); Discard and Reset delete them, so a saved look never changes without Save.
    /// </summary>
    public sealed class StagedFiles
    {
        private readonly Dictionary<string, string> _final = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // staged -> fixed name

        public StagedFiles(string folder) => Folder = folder;

        public string Folder { get; }

        public bool IsEmpty => _final.Count == 0;

        public bool IsStaged(string path) => _final.ContainsKey(LookFiles.Full(path));

        /// <summary>A new pending path for a file that becomes <paramref name="finalPath"/> on commit; one staged earlier for it is deleted.</summary>
        public string Stage(string finalPath)
        {
            var target = LookFiles.Full(finalPath);
            foreach (var old in _final.Where(p => string.Equals(p.Value, target, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToList())
            {
                Delete(old);
                _final.Remove(old);
            }
            Directory.CreateDirectory(Folder);
            var path = LookFiles.Full(Path.Combine(Folder, Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(finalPath)));
            _final[path] = target;
            return path;
        }

        /// <summary>A copy of the block naming the fixed files instead of the staged ones (nothing is moved). The input is not changed.</summary>
        public MaterialOverride Final(MaterialOverride block) =>
            MaterialTable.WithPaths(block, file => _final.TryGetValue(LookFiles.Full(file), out var target) ? target : file);

        /// <summary>Moves each staged file the block uses over its fixed name (replacing it), then deletes the rest.</summary>
        public void Commit(MaterialOverride block)
        {
            var used = new HashSet<string>(LookFiles.Files(block).Select(LookFiles.Full), StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _final.Where(p => used.Contains(p.Key) && File.Exists(p.Key)))
            {
                var directory = Path.GetDirectoryName(pair.Value);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                var temp = pair.Value + ".tmp";
                File.Copy(pair.Key, temp, true);
                if (File.Exists(pair.Value)) File.Replace(temp, pair.Value, null);
                else File.Move(temp, pair.Value);
            }
            Discard();
        }

        /// <summary>Deletes every staged file.</summary>
        public void Discard()
        {
            foreach (var path in _final.Keys) Delete(path);
            _final.Clear();
        }

        /// <summary>Deletes what an earlier run left in the pending folder.</summary>
        public void Clean()
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var file in Directory.GetFiles(Folder)) Delete(file);
        }

        private static void Delete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // left for Clean on the next start
            }
        }
    }
}
