using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Tyrant.Framework.Core
{
    /// <summary>What the in-game editor's Export mask PNG and Export colours PNG write.</summary>
    public enum ExportKind { IridescenceMask, FurMask, BabyFurMask, Colours }

    /// <summary>How Export mask PNG writes the iridescence mask an animal shows.</summary>
    public enum MaskExportMode
    {
        /// <summary>Nothing to write (a mask from a skin map or a PNG that is not read yet).</summary>
        None,

        /// <summary>The texture the animal shows, as a grey PNG.</summary>
        Image,

        /// <summary>An all-white PNG: the shimmer shows on the whole body.</summary>
        White,

        /// <summary>Another species' mask: it stays a reference and is never written.</summary>
        SpeciesReference,
    }

    /// <summary>Whether Export colours PNG can write the shimmer colours shown, and why not.</summary>
    public enum ColoursExport { Ok, Game, Species, File }

    /// <summary>
    /// The editor's exports: they go to UserData/Tyrant/Masks under readable names ("Tyrannosaurus Leatherhide fur mask.png")
    /// that never overwrite a file there; a save into a mod copies them into the owner's folder under the fixed names.
    /// </summary>
    public static class MaskExports
    {
        /// <summary>The size of a colours strip: x runs N·V 0 to 1, every row the same.</summary>
        public const int RampWidth = 256, RampHeight = 8;

        private static readonly char[] Invalid = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

        /// <summary>The words that end an export's name.</summary>
        public static string Describe(ExportKind kind) => kind switch
        {
            ExportKind.IridescenceMask => "iridescence mask",
            ExportKind.FurMask => "fur mask",
            ExportKind.BabyFurMask => "baby fur mask",
            _ => "colours",
        };

        /// <summary>The name an export takes in a mod's material folder, as the app's Import image names it.</summary>
        public static string FixedName(ExportKind kind) => kind switch
        {
            ExportKind.IridescenceMask => LookEditing.IridescenceMaskFile,
            ExportKind.FurMask => LookEditing.FurMaskFile,
            ExportKind.BabyFurMask => "fur-infant-mask.png",
            _ => "iridescence-ramp.png",
        };

        /// <summary>
        /// "{species} {skin} {kind}.png": a look of one animal adds its name when it has one, a species' look names only the
        /// species. Characters a file name cannot hold become "_".
        /// </summary>
        public static string FileName(LookScope scope, string species, string? skin, string? animalName, ExportKind kind)
        {
            var parts = new List<string?> { species };
            if (scope != LookScope.Species) parts.Add(skin);
            if (scope == LookScope.Animal) parts.Add(animalName);
            parts.Add(Describe(kind));
            return string.Join(" ", parts.Select(Clean).Where(p => p.Length > 0)) + ".png";
        }

        /// <summary>The name itself when no file in <paramref name="taken"/> has it (any case), else the first free "name 2", "name 3", ….</summary>
        public static string Unique(string fileName, IEnumerable<string> taken)
        {
            var names = new HashSet<string>(taken.Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(fileName)) return fileName;
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            for (var n = 2; ; n++)
            {
                var candidate = stem + " " + n + extension;
                if (!names.Contains(candidate)) return candidate;
            }
        }

        /// <summary>
        /// What Export mask PNG writes for the iridescence mask <paramref name="shown"/> (null: no layer sets one) when the
        /// animal has a mask texture or not: Everywhere and no texture at all are a white mask.
        /// </summary>
        public static MaskExportMode IridescenceMode(MaskSource? shown, bool hasTexture)
        {
            if (shown?.Species != null) return MaskExportMode.SpeciesReference;
            if (shown?.Everywhere == true) return MaskExportMode.White;
            if (hasTexture) return MaskExportMode.Image;
            return shown == null || shown.Game ? MaskExportMode.White : MaskExportMode.None;
        }

        /// <summary>The colours a ramp exports (inline colours, or a thin film's); the game's, a species' and a PNG are not.</summary>
        public static ColoursExport ColoursFrom(RampSource? ramp, out IReadOnlyList<Rgb>? stops)
        {
            stops = null;
            switch (LookEditing.RampKindOf(ramp))
            {
                case RampKind.Game:
                    return ColoursExport.Game;
                case RampKind.Species:
                    return ColoursExport.Species;
                case RampKind.File:
                    return ColoursExport.File;
                default:
                    stops = IridescenceRamp.Stops(ramp!);
                    return stops == null || stops.Count == 0 ? ColoursExport.Game : ColoursExport.Ok;
            }
        }

        private static string Clean(string? part)
        {
            if (string.IsNullOrEmpty(part)) return "";
            var text = new StringBuilder(part!.Length);
            foreach (var c in part) text.Append(Array.IndexOf(Invalid, c) >= 0 ? '_' : char.IsControl(c) ? ' ' : c);
            return string.Join(" ", text.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.');
        }
    }

    /// <summary>The fixed-name images one Save wrote into a mod, with backups of those they replaced, until the save is done.</summary>
    public sealed class ExportCommit
    {
        private readonly List<(string Target, string? Backup)> _written = new List<(string, string?)>();

        internal void Replaced(string target, string? backup) => _written.Add((target, backup));

        /// <summary>The save failed: each replaced image comes back from its backup, and one that was new is deleted.</summary>
        public void Undo()
        {
            for (var i = _written.Count - 1; i >= 0; i--)
            {
                var (target, backup) = _written[i];
                try
                {
                    if (backup == null)
                    {
                        if (File.Exists(target)) File.Delete(target);
                    }
                    else if (File.Exists(backup))
                    {
                        File.Copy(backup, target, true);
                        File.Delete(backup);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // nothing more can be done; the backup, if any, stays next to it
                }
            }
            _written.Clear();
        }

        /// <summary>The save went through: the backups go.</summary>
        public void Keep()
        {
            foreach (var (_, backup) in _written)
            {
                try
                {
                    if (backup != null && File.Exists(backup)) File.Delete(backup);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // a stale .bak is harmless
                }
            }
            _written.Clear();
        }
    }

    /// <summary>
    /// The exports a draft made, by kind. Saving into a mod names each one the block uses in its own slot by its fixed name in
    /// the owner's folder and copies it there (over the saved look's own); the export stays in Masks. Forgetting deletes nothing:
    /// the file is the player's, and a saved look's fixed-name image changes only on Save.
    /// </summary>
    public sealed class ExportedFiles
    {
        private readonly Dictionary<string, ExportKind> _kinds = new Dictionary<string, ExportKind>(StringComparer.OrdinalIgnoreCase);

        public void Add(string file, ExportKind kind) => _kinds[LookFiles.Full(file)] = kind;

        public void Forget() => _kinds.Clear();

        /// <summary>A copy of the block naming the fixed files in <paramref name="folder"/> instead of the exports (nothing is copied).</summary>
        public MaterialOverride Final(MaterialOverride block, string folder)
        {
            var copy = MaterialTable.WithPaths(block, file => file);
            foreach (var (source, kind) in Slots(copy))
                if (Exported(source.File, kind)) source.File = Path.Combine(folder, MaskExports.FixedName(kind));
            if (copy.Iridescence?.Ramp is RampSource ramp && Exported(ramp.File, ExportKind.Colours))
                ramp.File = Path.Combine(folder, MaskExports.FixedName(ExportKind.Colours));
            return copy;
        }

        /// <summary>
        /// Copies each export the block uses in its own slot over its fixed name in <paramref name="folder"/>. The images it
        /// replaces are backed up until the save is done: <see cref="ExportCommit.Undo"/> when the rest of the save fails,
        /// <see cref="ExportCommit.Keep"/> when it went through. A failure here undoes what it copied before throwing.
        /// </summary>
        public ExportCommit Commit(MaterialOverride block, string folder)
        {
            var files = Slots(block).Select(s => (s.Source.File, s.Kind)).ToList();
            if (block.Iridescence?.Ramp?.File is string ramp) files.Add((ramp, ExportKind.Colours));
            var commit = new ExportCommit();
            try
            {
                foreach (var (file, kind) in files)
                {
                    if (!Exported(file, kind) || !File.Exists(file)) continue;
                    Directory.CreateDirectory(folder);
                    var target = Path.Combine(folder, MaskExports.FixedName(kind));
                    var temp = target + ".tmp";
                    File.Copy(file!, temp, true);
                    if (File.Exists(target))
                    {
                        var backup = target + ".bak";
                        File.Replace(temp, target, backup);
                        commit.Replaced(target, backup);
                    }
                    else
                    {
                        File.Move(temp, target);
                        commit.Replaced(target, null);
                    }
                }
            }
            catch
            {
                commit.Undo();
                throw;
            }
            return commit;
        }

        private bool Exported(string? file, ExportKind kind) => file != null && _kinds.TryGetValue(LookFiles.Full(file), out var k) && k == kind;

        private static IEnumerable<(MaskSource Source, ExportKind Kind)> Slots(MaterialOverride block)
        {
            if (block.Iridescence?.Mask is MaskSource mask) yield return (mask, ExportKind.IridescenceMask);
            if (block.Fur?.Mask is MaskSource fur) yield return (fur, ExportKind.FurMask);
            if (block.Fur?.InfantMask is MaskSource infant) yield return (infant, ExportKind.BabyFurMask);
        }
    }
}
