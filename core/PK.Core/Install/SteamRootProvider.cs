using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PK.Core.Install;

public interface ISteamRootProvider
{
    /// <summary>Steam's install folder, or null when Steam is not installed.</summary>
    string? GetSteamRoot();
}

[SupportedOSPlatform("windows")]
public sealed class RegistrySteamRootProvider : ISteamRootProvider
{
    public string? GetSteamRoot()
    {
        using (var user = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            if (user?.GetValue("SteamPath") is string p && Directory.Exists(p))
                return Path.GetFullPath(p);
        using (var machine = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
            if (machine?.GetValue("InstallPath") is string q && Directory.Exists(q))
                return Path.GetFullPath(q);
        return null;
    }
}
