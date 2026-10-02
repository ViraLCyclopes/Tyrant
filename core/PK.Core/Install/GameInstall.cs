namespace PK.Core.Install;

/// <summary>Paths of one Prehistoric Kingdom install. RootDir is a full path without a trailing separator.</summary>
public sealed record GameInstall(string RootDir, string? SteamAppId)
{
    public const string ExeName = "Prehistoric Kingdom.exe";
    public const string DataDirName = "Prehistoric Kingdom_Data";

    public string DataDir => Path.Combine(RootDir, DataDirName);
    public string ManagedDir => Path.Combine(DataDir, "Managed");
    public string StreamingAssetsDir => Path.Combine(DataDir, "StreamingAssets");
    public string BootConfigPath => Path.Combine(DataDir, "boot.config");
    public string AssemblyCSharpPath => Path.Combine(ManagedDir, "Assembly-CSharp.dll");
}
