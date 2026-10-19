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

        public static RampKind RampKindOf(RampSource? ramp) =>
            ramp == null ? RampKind.Game : ramp.File != null ? RampKind.File : ramp.ThinFilm != null ? RampKind.ThinFilm : RampKind.Stops;

        public static MaskKind MaskKindOf(MaskSource? mask) =>
            mask == null ? MaskKind.Game : mask.Everywhere ? MaskKind.Everywhere : mask.File != null ? MaskKind.File : MaskKind.Channel;

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

        /// <param name="effective">The animal's merged block now (the draft's layer included), or null.</param>
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
            var seed = !gameHasRamp && below?.Ramp == null && own?.Ramp == null && !Shaped(below) && !Shaped(own);
            draft.SetIridescenceOn(true);
            if (!seed) return;
            draft.SetThinFilm(LookEditing.DefaultThickness);
            draft.SetShaping(LookEditing.StartScale, LookEditing.StartPower, LookEditing.StartBias);
        }

        private static bool Shaped(IridescenceSettings? i) => i != null && (i.Scale != null || i.Power != null || i.Bias != null);
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
        /// name and other content is kept, the copy gets "-2", "-3"; the same content already there is reused.
        /// </summary>
        public static MaterialOverride Adopt(MaterialOverride block, string modDir, string ownerFolder)
        {
            var folder = Path.Combine(modDir, ownerFolder.Replace('/', Path.DirectorySeparatorChar));
            return MaterialTable.WithPaths(block, file =>
            {
                if (ModPaths.IsInside(file, modDir)) return RelativePath(file, modDir);
                if (!File.Exists(file)) return null; // a missing file outside the mod cannot be carried in
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

        private static string RelativePath(string file, string baseDir)
        {
            var root = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(file).Substring(root.Length).Replace('\\', '/');
        }
    }
}
