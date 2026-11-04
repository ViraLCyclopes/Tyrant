using System;
using System.Globalization;
using System.IO;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// A file in UserData/Tyrant saying the coming game start is a data dump, so the framework applies no mods: mods' skins and
    /// species would otherwise be dumped as the game's, and modders would pick skin numbers only their own game has. It is not the
    /// dumper's request because the dumper deletes that as soon as it starts, which may be before the framework looks. It expires
    /// like the request does, so one left behind by a crash never costs a later normal game start its mods.
    /// </summary>
    public static class DumpMarker
    {
        public const string FileName = "dump-in-progress.txt";

        public static void Write(string tyrantDirectory, DateTime expiresUtc) =>
            File.WriteAllText(Path.Combine(tyrantDirectory, FileName), expiresUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));

        public static void Remove(string tyrantDirectory)
        {
            var path = Path.Combine(tyrantDirectory, FileName);
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>False for a missing, expired or unreadable marker: when in doubt the player keeps their mods.</summary>
        public static bool IsActive(string tyrantDirectory, DateTime utcNow)
        {
            try
            {
                var path = Path.Combine(tyrantDirectory, FileName);
                return File.Exists(path)
                    && DateTime.TryParse(File.ReadAllText(path).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expires)
                    && utcNow <= expires.ToUniversalTime();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
