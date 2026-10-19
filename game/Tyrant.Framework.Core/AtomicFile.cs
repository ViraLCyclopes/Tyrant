using System.IO;
using System.Text;

namespace Tyrant.Framework.Core
{
    /// <summary>Writes a file so a crash or a full disk never leaves half of it.</summary>
    public static class AtomicFile
    {
        /// <summary>Writes UTF-8 (no byte-order mark) to path + ".tmp", then moves it over <paramref name="path"/>.</summary>
        public static void Write(string path, string text)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }
    }
}
