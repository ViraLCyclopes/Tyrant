using Tyrant.Core.Errors;
using Tyrant.Core.Workspaces;
using Tyrant.Framework.Core;

namespace Tyrant.Core.Mods;

public sealed record OtherSpecies(string Key, string ModId, string ModName, SpeciesEntry Entry);
public sealed record OtherModState(string Name, bool InWorkspace, bool Installed, bool Enabled);
/// <summary>Mods is every other mod found (with or without species): Check uses it to tell "not in that mod" from "mod not here".</summary>
public sealed record OtherMods(IReadOnlyList<OtherSpecies> Species, IReadOnlyDictionary<string, OtherModState> Mods);

/// <summary>Custom species of the other mods in the workspace and installed in the game; the workspace copy wins.</summary>
public static class OtherModSpecies
{
    public static OtherMods Load(Workspace ws, IReadOnlyList<InstalledMod> installed, string? exceptModId)
    {
        var species = new Dictionary<string, OtherSpecies>(StringComparer.Ordinal);
        var mods = new Dictionary<string, OtherModState>(StringComparer.Ordinal);
        foreach (var id in ModProject.Ids(ws).Where(id => id != exceptModId))
        {
            ModProject mod;
            try { mod = ModProject.Open(ws, id); }
            catch (TyrantException) { continue; } // an unreadable mod offers nothing; its own page reports why
            mods[id] = new OtherModState(mod.Manifest.Name, true, false, false);
            foreach (var entry in mod.Manifest.Species) species[entry.Key(id)] = new OtherSpecies(entry.Key(id), id, mod.Manifest.Name, entry);
        }
        foreach (var m in installed.Where(m => m.Error is null && m.Id != exceptModId))
        {
            if (mods.TryGetValue(m.Id, out var known))
            {
                mods[m.Id] = known with { Installed = true, Enabled = m.Enabled };
                continue;
            }
            ModManifest manifest;
            try
            {
                manifest = ModManifest.Parse(File.ReadAllText(Path.Combine(m.Dir, ModManifest.FileName)));
                manifest.Species.AddRange(AnimalDatabaseFile.Read(Path.Combine(m.Dir, AnimalDatabaseFile.FileName)));
            }
            catch (Exception ex) when (ex is ManifestException or IOException or UnauthorizedAccessException) { continue; }
            mods[m.Id] = new OtherModState(manifest.Name, false, true, m.Enabled);
            foreach (var entry in manifest.Species) species[entry.Key(m.Id)] = new OtherSpecies(entry.Key(m.Id), m.Id, manifest.Name, entry);
        }
        return new OtherMods(species.Values.OrderBy(s => s.Key, StringComparer.Ordinal).ToList(), mods);
    }
}
