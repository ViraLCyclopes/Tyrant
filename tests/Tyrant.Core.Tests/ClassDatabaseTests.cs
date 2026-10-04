using AssetsTools.NET.Extra;
using Tyrant.Core.Assets;

namespace Tyrant.Core.Tests;

public class ClassDatabaseTests
{
    [Fact]
    public void The_embedded_class_database_knows_the_games_unity_version()
    {
        var manager = new AssetsManager();
        manager.LoadClassPackage(ClassDatabase.Open());

        var database = manager.LoadClassDatabaseFromPackage("2022.3.62f3");

        Assert.NotNull(database);
        Assert.NotNull(database.FindAssetClassByID((int)AssetClassID.Texture2D));
        Assert.NotNull(database.FindAssetClassByID((int)AssetClassID.Material));
        Assert.NotNull(database.FindAssetClassByID((int)AssetClassID.GameObject));
    }
}
