using System;
using System.IO;
using System.Linq;

namespace Tyrant.Framework.Core
{
    /// <summary>An authored PK animal prefab in a self-contained Windows AssetBundle.</summary>
    public sealed class SpeciesPrefab
    {
        public string Bundle { get; set; } = "";
        public string Asset { get; set; } = "";

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Bundle) || Path.IsPathRooted(Bundle) || Bundle.Contains(":") ||
                Bundle.Replace('\\', '/').Split('/').Any(p => p == ".." || p == "." || p.Length == 0) ||
                !Bundle.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
                throw new ManifestException("Species prefab bundle must be a relative .bundle path inside the mod.");
            if (string.IsNullOrWhiteSpace(Asset) || !Asset.StartsWith("assets/", StringComparison.Ordinal) ||
                !Asset.EndsWith(".prefab", StringComparison.Ordinal) || Asset.Contains("\\") || Asset.Contains(":") ||
                Asset.Split('/').Any(p => p == ".." || p == "." || p.Length == 0) || Asset != Asset.ToLowerInvariant())
                throw new ManifestException("Species prefab asset must be its lowercase Unity asset path, e.g. assets/animals/mirabilis.prefab.");
        }
    }
}
