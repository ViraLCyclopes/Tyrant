using StbImageSharp;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Sounds;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

/// <summary>Errors block an install; warnings are shown but do not.</summary>
public sealed record ModCheckResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool Ok => Errors.Count == 0;

    /// <summary>Mod-relative colour PNGs without the transparency their vanilla texture cuts feathers or hair out with.</summary>
    public IReadOnlyList<string> MissingCutouts { get; init; } = [];
}

/// <summary>Finds what would go wrong in the game before a mod is installed.</summary>
public sealed class ModChecker(Func<AssetRecord, (int Width, int Height)?> sizeOf, Func<AssetRecord, ImageResult?>? pixelsOf = null, Func<AssetRecord, ImageResult?>? fullPixelsOf = null)
{
    /// <summary>Reads original texture sizes from the game's bundles; a texture that cannot be read just skips the size check.</summary>
    public static ModChecker ForGame(GameInstall install) => new(texture =>
    {
        try
        {
            using var session = new AssetSession(install);
            var facts = TextureFacts.Read(session.Open(texture).BaseField);
            return (facts.Width, facts.Height);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }, Cutouts.GamePixels(install, new BundleAssetReader()), Cutouts.GamePixels(install, new BundleAssetReader(), onlyIfAlpha: false));

    public ModCheckResult Check(ModProject mod, AssetIndex? index, IReadOnlyList<SpeciesSkins>? species = null, SoundCatalog? sounds = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (mod.Manifest.Replace.Count == 0 && mod.Manifest.Skins.Count == 0 && mod.Manifest.Models.Count == 0 && mod.Manifest.Sounds.Count == 0
            && mod.Manifest.Assembly is null)
            warnings.Add("The mod does nothing yet: add a texture replacement, a skin, a model or a sound.");
        if (index is null && mod.Manifest.Replace.Count > 0)
            warnings.Add("There is no asset index, so the target textures were not checked. Click Index assets on the Workspace tab.");

        foreach (var group in mod.Manifest.Replace.GroupBy(r => r.Texture, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            warnings.Add($"{group.Key} is replaced {group.Count()} times; only the last one is used.");

        foreach (var entry in mod.Manifest.Replace)
        {
            var image = Image(mod, entry.Texture, entry.File, errors);
            if (image is null) continue;

            if (index is not null)
            {
                var target = index.Assets.FirstOrDefault(a => a.Type == "Texture2D" && string.Equals(a.Name, entry.Texture, StringComparison.OrdinalIgnoreCase));
                if (target is null)
                {
                    errors.Add($"{entry.Texture} is not in the asset index; check the name (the game would never use this file).");
                    continue;
                }
                var copies = index.Assets.Where(a => a.Type == "Texture2D" && string.Equals(a.Name, entry.Texture, StringComparison.OrdinalIgnoreCase)).ToList();
                if (copies.Count > 1)
                    warnings.Add($"{entry.Texture}: {copies.Count} textures have this name ({string.Join(", ", copies.Select(c => c.DataFile ?? c.Bundle).Distinct(StringComparer.OrdinalIgnoreCase))}); the game replaces all of them.");
                if (sizeOf(target) is { } original && (original.Width, original.Height) != (image.Width, image.Height))
                    warnings.Add($"{entry.Texture}: {entry.File} is {image.Width}x{image.Height} but the original is {original.Width}x{original.Height}; it will look stretched or blurry.");
            }

            if (NormalMap.IsCandidate(entry.Texture) && NormalMaps.Classify(image.Data) == NormalMapKind.Unknown)
                warnings.Add($"{entry.Texture}: {entry.File} does not look like a normal map (neither the blue-purple standard form nor Unity's packed form).");
        }

        if (species is null && mod.Manifest.Skins.Count > 0)
            warnings.Add("There is no data dump, so the skins' species and base skins were not checked (Workspace → Run data dump).");
        foreach (var skin in mod.Manifest.Skins)
        {
            var key = skin.Key(mod.Id);
            VanillaSkin? based = null;
            if (species is not null)
            {
                var target = species.FirstOrDefault(s => string.Equals(s.SpeciesId, skin.Species, StringComparison.OrdinalIgnoreCase));
                if (target is null) errors.Add($"{key}: species \"{skin.Species}\" is not in the game data.");
                else if (!string.Equals(target.SpeciesId, skin.Species, StringComparison.Ordinal))
                    errors.Add($"{key}: species \"{skin.Species}\" must be written \"{target.SpeciesId}\" (the game matches the exact spelling).");
                else
                {
                    based = int.TryParse(skin.Base, out var number) ? target.Skins.FirstOrDefault(s => s.Index == number)
                        : target.Skins.FirstOrDefault(s => string.Equals(s.Name, skin.Base, StringComparison.OrdinalIgnoreCase));
                    if (based is null) errors.Add($"{key}: base skin \"{skin.Base}\" is not one of {target.SpeciesId}'s skins ({string.Join(", ", target.Skins.Select(s => s.Name))}).");
                }
            }
            if (skin.Thumbnail is not null) Image(mod, $"{key} thumbnail", skin.Thumbnail, errors);
            foreach (var (sex, files, baseTextures) in new[] { ("male", skin.Male, based?.Male), ("female", skin.Female, based?.Female) })
            {
                if (files is null) continue;
                foreach (var (slot, file) in files)
                {
                    var image = Image(mod, $"{key} {sex} {slot}", file, errors);
                    if (image is null) continue;
                    if (index is not null && baseTextures is not null && baseTextures.TryGetValue(slot, out var guid)
                        && index.Assets.FirstOrDefault(a => string.Equals(a.Guid, guid, StringComparison.OrdinalIgnoreCase)) is { } original
                        && sizeOf(original) is { } size && (size.Width, size.Height) != (image.Width, image.Height))
                        warnings.Add($"{key} {sex} {slot}: {file} is {image.Width}x{image.Height} but the base texture is {size.Width}x{size.Height}.");
                    if (SkinSlotNames.KindOf(slot) == SlotKind.Normal && NormalMaps.Classify(image.Data) == NormalMapKind.Unknown)
                        warnings.Add($"{key} {sex} {slot}: {file} does not look like a normal map.");
                }
            }
        }
        ColourWarnings(mod, index, species, warnings);
        ModelProblems(mod, species, errors, warnings);
        SoundProblems(mod, species, sounds, errors, warnings);
        var missing = index is null || pixelsOf is null ? [] : MissingCutouts(mod, index, species, warnings);
        return new ModCheckResult(errors, warnings) { MissingCutouts = missing };
    }

    public const long LargeSoundBytes = 20L * 1024 * 1024;

    /// <summary>Each sound replacement's files, scope and event (against the dump's sound lists and species when there is a dump).</summary>
    private static void SoundProblems(ModProject mod, IReadOnlyList<SpeciesSkins>? species, SoundCatalog? sounds, List<string> errors, List<string> warnings)
    {
        foreach (var sound in mod.Manifest.Sounds)
        {
            var label = $"Sound {sound.Event} {ModProject.ScopeText(sound.Species, sound.Skin)}";
            if (sound.Species is not null && sound.Skin is not null)
                errors.Add($"{label}: it names both a species and a skin; keep one (a skin already belongs to its species).");
            if (sound.Files.Count == 0) errors.Add($"{label}: it has no files; pick an audio file or remove it.");
            foreach (var file in sound.Files)
            {
                var path = Path.Combine(mod.Dir, file);
                if (!mod.IsInsideMod(file)) { errors.Add($"{label}: {file} is outside the mod folder."); continue; }
                if (!File.Exists(path)) { errors.Add($"{label}: {file} is missing; replace the sound again."); continue; }
                byte[] head;
                using (var stream = File.OpenRead(path))
                {
                    head = new byte[16];
                    head = head[..stream.Read(head, 0, head.Length)];
                }
                if (AudioFormat.Sniff(head) is null) { errors.Add($"{label}: {file} is not an audio file (WAV, OGG, MP3 or FLAC)."); continue; }
                var size = new FileInfo(path).Length;
                if (size > LargeSoundBytes)
                    warnings.Add($"{label}: {file} is {size / (1024 * 1024)} MB; the game loads it into memory, so a shorter or compressed (OGG) file is better.");
            }

            if (sound.Chance == 0)
                warnings.Add($"{label}: its chance is 0, so it never plays; raise the chance or choose Like the game on its page.");
            if (sound.Chance is not null && sounds?.Find(sound.Event) is { OneShot: false })
                warnings.Add($"{label}: it loops, and a chance is only for one-off sounds; it plays while the game's sound does.");
            if (sounds?.Find(sound.Event) is { PerAnimal: false } menuSound && sound.IsUnique)
                warnings.Add($"{label}: {menuSound.Name.ToLowerInvariant()} plays in menus or without an animal, so only a replacement for everyone applies; choose Everyone on its page.");
            if (sounds is not null && sounds.Find(sound.Event) is null)
                warnings.Add(sounds.HasEventList
                    ? $"{label}: {sound.Event} is not one of the game's sounds, so it never plays; copy the name from a species' Sounds list or All sounds."
                    : $"{label}: {sound.Event} is not in the species sound lists. To check every game sound, on the Workspace tab click Run data dump once more.");

            if (species is null) continue;
            if (sound.Species is { } speciesId)
            {
                var target = species.FirstOrDefault(s => string.Equals(s.SpeciesId, speciesId, StringComparison.OrdinalIgnoreCase));
                if (target is null) warnings.Add($"{label}: species \"{speciesId}\" is not in the game data, so it never plays.");
                else if (!string.Equals(target.SpeciesId, speciesId, StringComparison.Ordinal))
                    warnings.Add($"{label}: species \"{speciesId}\" must be written \"{target.SpeciesId}\" (the game matches the exact spelling).");
            }
            if (sound.Skin is { } skinKey && UnknownSkin(mod, species, skinKey) is { } problem) warnings.Add($"{label}: {problem}");
        }
    }

    /// <summary>A skin key is "&lt;mod&gt;/&lt;skin id&gt;" (a Tyrant skin) or "&lt;species&gt;/&lt;skin name&gt;" (a game skin); other mods' skins cannot be checked here.</summary>
    private static string? UnknownSkin(ModProject mod, IReadOnlyList<SpeciesSkins> species, string key)
    {
        var slash = key.IndexOf('/');
        if (slash <= 0 || slash == key.Length - 1)
            return $"skin \"{key}\" is not a skin key; pick the skin from the list (it looks like species/skin name or mod/skin).";
        var (owner, name) = (key[..slash], key[(slash + 1)..]);
        if (string.Equals(owner, mod.Id, StringComparison.Ordinal))
            return mod.Manifest.Skins.Any(s => s.Id == name) ? null : $"skin \"{key}\" is not one of this mod's skins.";
        var target = species.FirstOrDefault(s => string.Equals(s.SpeciesId, owner, StringComparison.Ordinal));
        if (target is null) return null;
        return target.Skins.Any(s => string.Equals(s.Name, name, StringComparison.Ordinal))
            ? null
            : $"skin \"{key}\" is not one of {target.SpeciesId}'s skins ({string.Join(", ", target.Skins.Select(s => s.Name))}).";
    }

    /// <summary>Opaque colour PNGs whose vanilla texture is partly see-through; the vanilla one is only decoded for those.</summary>
    /// <summary>Each model's file, species and build report (built by Replace, rebuilt by Check and Install when its .glb changed).</summary>
    private static void ModelProblems(ModProject mod, IReadOnlyList<SpeciesSkins>? species, List<string> errors, List<string> warnings)
    {
        foreach (var (speciesId, file) in mod.AllModelEntries())
        {
            if (!mod.IsInsideMod(file))
            {
                errors.Add($"Model of {speciesId}: {file} points outside the mod; a mod may only use its own files.");
                continue;
            }
            if (species is not null && !species.Any(s => s.SpeciesId == speciesId))
                errors.Add($"Model of {speciesId}: species \"{speciesId}\" is not in the game data.");
            if (!File.Exists(Path.Combine(mod.Dir, file)))
            {
                errors.Add($"Model of {speciesId}: {file} is missing; replace the model again (Mods tab → open the mod → Models, or 'tyrant mod replace-model').");
                continue;
            }
            var report = Tyrant.Core.ModelReplacements.ModelBuilder.ReadReport(mod.Dir, file);
            if (report is null || Tyrant.Core.ModelReplacements.ModelBuilder.IsStale(mod.Dir, file, mod.RigOfFile(file)))
            {
                warnings.Add($"Model of {speciesId}: {file} changed since it was built; Check and Install rebuild it.");
                continue;
            }
            errors.AddRange(report.Errors.Select(e => $"Model of {speciesId}: {e}"));
            warnings.AddRange(report.Warnings.Select(w => $"Model of {speciesId}: {w}"));
            if (Tyrant.Core.ModelReplacements.ModelBuilder.OriginChanged(report))
                warnings.Add($"Model of {speciesId}: your file {report.Origin} changed since you added it; to use the new version click Re-import on the model's page (Mods tab → open the mod → Models), or run 'tyrant mod replace-model' again.");
        }
    }

    private List<string> MissingCutouts(ModProject mod, AssetIndex index, IReadOnlyList<SpeciesSkins>? species, List<string> warnings)
    {
        var missing = new List<string>();
        var vanillaShare = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, vanillas) in Cutouts.ByFile(mod, index, species))
        {
            var path = Path.GetFullPath(Path.Combine(mod.Dir, file));
            if (!ModPaths.IsInside(path, mod.Dir) || !File.Exists(path)) continue;
            double share;
            try
            {
                using var stream = File.OpenRead(path);
                share = Cutouts.SeeThrough(ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha).Data);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
            {
                continue; // already reported as unreadable
            }
            if (share >= Cutouts.Lost) continue;
            AssetRecord? vanilla = null;
            var original = 0.0;
            foreach (var candidate in vanillas) // the one with the most cutouts decides
            {
                var key = candidate.Guid ?? $"{candidate.Bundle}#{candidate.PathId}";
                if (!vanillaShare.TryGetValue(key, out var candidateShare))
                    vanillaShare[key] = candidateShare = pixelsOf!(candidate) is { } pixels ? Cutouts.SeeThrough(pixels.Data) : 0;
                if (candidateShare > original) (vanilla, original) = (candidate, candidateShare);
            }
            if (vanilla is null || original < Cutouts.UsesCutouts) continue;
            missing.Add(file);
            warnings.Add($"{file} has no see-through pixels, but {vanilla.Name} cuts feathers or hair out with them ({original:P0} of it is see-through): " +
                         "in game those parts would show as solid shapes. Keep the alpha channel when saving, or use Restore cutouts.");
        }
        return missing;
    }

    /// <summary>Checks one PNG of the mod; returns the decoded image, or null after adding an error.</summary>
    private static ImageResult? Image(ModProject mod, string label, string file, List<string> errors)
    {
        var path = Path.GetFullPath(Path.Combine(mod.Dir, file));
        if (!ModPaths.IsInside(path, mod.Dir)) { errors.Add($"{label}: \"{file}\" points outside the mod folder."); return null; }
        if (!File.Exists(path)) { errors.Add($"{label}: {file} is missing."); return null; }
        try
        {
            if (!HasPngSignature(path)) { errors.Add($"{label}: {file} is not a PNG (other image formats are not supported in game)."); return null; }
            using var stream = File.OpenRead(path);
            return ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"{label}: {file} could not be read ({ex.Message}); is it open in another program?");
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            errors.Add($"{label}: {file} is not a readable PNG.");
            return null;
        }
    }

    /// <summary>Pattern colours that could never show (no red in the pattern map); extra maps that the game would colour as eyes (red above 0.9).</summary>
    private void ColourWarnings(ModProject mod, AssetIndex? index, IReadOnlyList<SpeciesSkins>? species, List<string> warnings)
    {
        foreach (var skin in mod.Manifest.Skins)
        {
            var key = skin.Key(mod.Id);
            var based = species is null ? null : BaseSkin(species, skin);
            foreach (var (sex, ownFiles, textures) in new[] { ("male", skin.Male, based?.Male), ("female", skin.Female, based?.Female) })
            {
                // A sex without its own files still wears the skin (the base skin's textures) and gets its pattern colours.
                var files = ownFiles ?? new Dictionary<string, string>();
                foreach (var (patternSlot, extraSlot) in new[] { ("pattern", "extra"), ("infantPattern", "infantExtra") })
                {
                    if (skin.Colors?.Pattern is not null)
                    {
                        var pattern = files.TryGetValue(patternSlot, out var patternFile) ? Decode(mod, patternFile) : Vanilla(index, textures, patternSlot);
                        if (pattern is not null && Share(pattern, p => p.R > 10) < 0.001)
                            warnings.Add($"{key} {sex}: colors.pattern is set, but the {patternSlot} map has no red anywhere, so the pattern colours would never show there. Paint red where they should go.");
                    }
                    if (files.TryGetValue(extraSlot, out var extraFile) && Decode(mod, extraFile) is { } extra && Vanilla(index, textures, extraSlot) is { } original)
                    {
                        var wrong = 0;
                        for (var y = 0; y < extra.Height; y++)
                        for (var x = 0; x < extra.Width; x++)
                        {
                            var o = original.Data[((y * original.Height / extra.Height) * original.Width + x * original.Width / extra.Width) * 4];
                            if (extra.Data[(y * extra.Width + x) * 4] > 230 && o <= 230) wrong++;
                        }
                        var share = (double)wrong / (extra.Width * extra.Height);
                        if (share > 0.005)
                            warnings.Add($"{key} {sex}: {extraFile} is brighter than 90% red outside the base skin's eyes ({share:P1} of it): the game colours those parts as eyes. Keep skin below 230 in the red channel.");
                    }
                }
            }
        }
    }

    private static VanillaSkin? BaseSkin(IReadOnlyList<SpeciesSkins> species, SkinEntry skin)
    {
        var target = species.FirstOrDefault(s => string.Equals(s.SpeciesId, skin.Species, StringComparison.OrdinalIgnoreCase));
        if (target is null) return null;
        return int.TryParse(skin.Base, out var number) ? target.Skins.FirstOrDefault(s => s.Index == number)
            : target.Skins.FirstOrDefault(s => string.Equals(s.Name, skin.Base, StringComparison.OrdinalIgnoreCase));
    }

    private ImageResult? Vanilla(AssetIndex? index, IReadOnlyDictionary<string, string>? textures, string slot) =>
        index is null || fullPixelsOf is null || textures is null || !textures.TryGetValue(slot, out var guid)
            || index.Assets.FirstOrDefault(a => string.Equals(a.Guid, guid, StringComparison.OrdinalIgnoreCase)) is not { } asset
            ? null
            : fullPixelsOf(asset);

    private static ImageResult? Decode(ModProject mod, string file)
    {
        var path = Path.GetFullPath(Path.Combine(mod.Dir, file));
        if (!ModPaths.IsInside(path, mod.Dir) || !File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
        {
            return null; // reported as unreadable elsewhere
        }
    }

    private static double Share(ImageResult image, Func<(byte R, byte G, byte B), bool> test)
    {
        var hits = 0;
        for (var i = 0; i < image.Data.Length; i += 4)
            if (test((image.Data[i], image.Data[i + 1], image.Data[i + 2]))) hits++;
        return (double)hits / (image.Width * image.Height);
    }

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    internal static bool HasPngSignature(string path)
    {
        using var stream = File.OpenRead(path);
        var head = new byte[PngSignature.Length];
        return stream.Read(head, 0, head.Length) == head.Length && head.AsSpan().SequenceEqual(PngSignature);
    }
}
