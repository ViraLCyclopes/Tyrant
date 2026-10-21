using System.ComponentModel;
using System.Globalization;
using Spectre.Console.Cli;
using Tyrant.Core.Data;
using Tyrant.Core.Errors;
using Tyrant.Core.Mods;
using Tyrant.Framework.Core;

namespace Tyrant.Cli.Commands;

public sealed class ModSizeCommand : Command<ModSizeCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandArgument(1, "<TARGET>")]
        [Description("The species: a game species id, or a custom species of this mod (tyrant.<mod>.<species>).")]
        public string Target { get; set; } = "";

        [CommandOption("--adult <X>")]
        [Description("Multiplies the adult size (0.05 to 20; past the size limit it is lowered in game).")]
        public double? Adult { get; set; }

        [CommandOption("--baby <X>")]
        [Description("The newborn's size as a share of the adult's (above 0, at most 1).")]
        public double? Baby { get; set; }

        [CommandOption("--male <X>")]
        [Description("Multiplies males' size (0.1 to 20).")]
        public double? Male { get; set; }

        [CommandOption("--female <X>")]
        [Description("Multiplies females' size (0.1 to 20).")]
        public double? Female { get; set; }

        [CommandOption("--clear")]
        [Description("Remove all of this species' size settings (before applying any value given with it).")]
        public bool Clear { get; set; }

        [CommandOption("--remove-record-edits")]
        [Description("A mod's own species: remove the old record edits of its growth curve and sex sizes, so only these sizes change it.")]
        public bool RemoveRecordEdits { get; set; }
    }

    private static string Text(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        if (settings.RemoveRecordEdits)
        {
            var removed = mod.RemoveSizeRecordEdits(settings.Target);
            Console.WriteLine(removed.Count == 0 ? $"{settings.Target} has no old record edits of its size." : "removed the old record edits: " + string.Join(", ", removed));
        }
        var current = settings.Clear ? null : mod.Sizes(settings.Target);
        if (settings.Clear || settings.Adult is not null || settings.Baby is not null || settings.Male is not null || settings.Female is not null)
        {
            var next = new SizeSettings
            {
                Target = settings.Target, Adult = settings.Adult ?? current?.Adult, Baby = settings.Baby ?? current?.Baby,
                Male = settings.Male ?? current?.Male, Female = settings.Female ?? current?.Female,
            };
            mod.SetSizes(settings.Target, next);
            current = mod.Sizes(settings.Target);
        }
        var shown = new[] { ("adult", current?.Adult), ("baby", current?.Baby), ("male", current?.Male), ("female", current?.Female) }
            .Where(v => v.Item2 is not null).Select(v => $"{v.Item1} {Text(v.Item2!.Value)}").ToList();
        Console.WriteLine($"{settings.Target} in '{settings.Id}': {(shown.Count == 0 ? "no size settings" : string.Join(", ", shown))}");
        if (mod.SizeRecordEdits(settings.Target) is { Count: > 0 } edits)
            Console.WriteLine($"old record edits: {string.Join(", ", edits)} (they change its size outside these settings; remove them with --remove-record-edits)");
        OriginalSize? original = null;
        try { original = SpeciesSizes.Original(DataStore.Open(ws), settings.Target, mod); }
        catch (TyrantException ex) when (ex.Code is TyrantErrorCode.DataMissing or TyrantErrorCode.AssetNotFound) { }
        if (original is null)
        {
            Console.WriteLine("original: unknown (no game data for this species; run 'tyrant dump run')");
            return ExitCodes.Ok;
        }
        var c1 = SizeMath.Evaluate(original.Curve, 1f);
        var parts = new List<string>();
        if (original.LengthHeightWidth is { Length: > 0 } lhw) parts.Add($"length {Text(lhw[0])}");
        parts.Add($"newborn {Text(c1 == 0f ? 1 : SizeMath.Evaluate(original.Curve, 0f) / (double)c1)} of the adult");
        if (original.Skins.Count > 0) parts.Add("skins (male/female) " + string.Join(", ", original.Skins.Select(s => $"{Text(s.Male)}/{Text(s.Female)}")));
        Console.WriteLine("original: " + string.Join("; ", parts));
        if (original.LengthHeightWidth is { Length: > 0 } length) Console.WriteLine($"final length {Text(length[0] * (current?.Adult ?? 1))}");
        return ExitCodes.Ok;
    }
}

public sealed class ModSizeRangesCommand : Command<ModSizeRangesCommand.Settings>
{
    public sealed class Settings : ModSettings
    {
        [CommandOption("--nursery <RANGE>")]
        [Description("The range of the Nursery size slider's genetic size, e.g. 0.9,1.1 (0 < min <= max <= 20).")]
        public string? Nursery { get; set; }

        [CommandOption("--breeding <RANGE>")]
        [Description("The range of the genetic size of bred young, e.g. 0.85,1.15.")]
        public string? Breeding { get; set; }

        [CommandOption("--limit <RANGE>")]
        [Description("The size limit on an animal's final scale, e.g. 0.05,20 (0.01 <= min <= 0.05, 2 <= max <= 20; the game's is 0.05,2). Among enabled mods the widest wins.")]
        public string? Limit { get; set; }

        [CommandOption("--clear")]
        [Description("Remove this mod's size ranges and size limit (before applying any range given with it).")]
        public bool Clear { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var (ws, _) = CliServices.OpenWorkspace(settings);
        var mod = ModProject.Open(ws, settings.Id);
        var nursery = Range("--nursery", settings.Nursery);
        var breeding = Range("--breeding", settings.Breeding);
        var limit = Range("--limit", settings.Limit);
        if (settings.Clear || nursery is not null || breeding is not null || limit is not null)
        {
            var old = settings.Clear ? null : mod.Manifest.SizeRanges;
            mod.SetSizeRanges(new SizeRanges { Nursery = nursery ?? old?.Nursery, Breeding = breeding ?? old?.Breeding, Limit = limit ?? old?.Limit });
        }
        var ranges = mod.Manifest.SizeRanges;
        static string Text(double[] r) => $"{r[0].ToString("0.###", CultureInfo.InvariantCulture)} to {r[1].ToString("0.###", CultureInfo.InvariantCulture)}";
        static string Show(double[]? r) => r is null ? "the game's" : Text(r);
        Console.WriteLine($"Size ranges of '{settings.Id}': nursery {Show(ranges?.Nursery)}, breeding {Show(ranges?.Breeding)}, "
            + $"limit {(ranges?.Limit is { } l ? Text(l) : $"the game's ({SizeLimit.Game})")}");
        return ExitCodes.Ok;
    }

    /// <summary>"min,max" as two numbers; null when the option was not given.</summary>
    private static double[]? Range(string option, string? text)
    {
        if (text is null) return null;
        var parts = text.Split(',');
        if (parts.Length != 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var min)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var max))
            throw new TyrantException(TyrantErrorCode.ModInvalid, $"{option} takes two numbers as <min>,<max> (got '{text}').");
        return [min, max];
    }
}
