using System.Collections.Generic;
using System.IO;

namespace Tyrant.Framework.Core
{
    /// <summary>The mod Tyrant creates for looks saved on vanilla skins and species: always loads last.</summary>
    public static class VanillaLooks
    {
        public const string Name = "Vanilla Looks";

        /// <summary>Creates &lt;modsDir&gt;/vanilla-looks/mod.json if missing (an existing one is left alone) and returns the folder.</summary>
        public static string EnsureMod(string modsDir)
        {
            var folder = Path.Combine(modsDir, ModCatalog.VanillaLooksId);
            var file = Path.Combine(folder, "mod.json");
            if (File.Exists(file)) return folder;
            Directory.CreateDirectory(folder);
            var manifest = new Dictionary<string, object?>
            {
                ["format"] = 1,
                ["id"] = ModCatalog.VanillaLooksId,
                ["name"] = Name,
            };
            AtomicFile.Write(file, Json.Write(manifest) + "\n");
            return folder;
        }

        /// <summary>This animal's look needs a saved park (its file is named after the save) and the animal's id; the other scopes can always be saved.</summary>
        public static bool IsAvailable(LookScope scope, ulong? animalId, string? saveName) =>
            scope != LookScope.Animal || (animalId != null && !string.IsNullOrWhiteSpace(saveName));
    }
}
