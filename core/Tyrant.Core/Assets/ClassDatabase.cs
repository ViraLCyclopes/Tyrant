namespace Tyrant.Core.Assets;

/// <summary>
/// Unity's class database (classdata.tpk, from AssetRipper/Tpk; see NOTICE): the field layout of every built-in type per
/// Unity version. Needed for the game's built-in files, which (unlike bundles) carry no type trees.
/// </summary>
internal static class ClassDatabase
{
    private const string Resource = "Tyrant.Core.Resources.classdata.tpk";

    public static Stream Open() =>
        typeof(ClassDatabase).Assembly.GetManifestResourceStream(Resource)
        ?? throw new InvalidOperationException($"The embedded {Resource} is missing from Tyrant.Core.");
}
