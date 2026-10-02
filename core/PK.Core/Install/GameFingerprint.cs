using System.Security.Cryptography;

namespace PK.Core.Install;

/// <summary>Identifies a game build; outputs stamped with a different fingerprint are stale.</summary>
public sealed record GameFingerprint(string BuildGuid, string AssemblySha256)
{
    private const string BuildGuidKey = "build-guid=";

    public static GameFingerprint Compute(GameInstall install)
    {
        string? guid = null;
        if (File.Exists(install.BootConfigPath))
        {
            foreach (var line in File.ReadLines(install.BootConfigPath))
            {
                if (line.StartsWith(BuildGuidKey, StringComparison.Ordinal))
                {
                    guid = line[BuildGuidKey.Length..].Trim();
                    break;
                }
            }
        }
        using var stream = File.OpenRead(install.AssemblyCSharpPath);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return new GameFingerprint(string.IsNullOrEmpty(guid) ? "unknown" : guid, hash);
    }
}
