using AssetsTools.NET;
using Tyrant.Core.Assets;
using Tyrant.Core.Install;
using Tyrant.Core.Workspaces;

namespace Tyrant.Core.Tests;

/// <summary>JSON files people open (dumps, reports, the workspace file) keep apostrophes, symbols and accents as typed.</summary>
public class ReadableJsonTests
{
    [Fact]
    public void An_asset_dump_keeps_apostrophes_and_accents()
    {
        var name = new AssetTypeTemplateField { Name = "m_Name", Type = "string", ValueType = AssetValueType.String, Children = [] };
        var root = new AssetTypeTemplateField { Name = "Base", Type = "Texture2D", ValueType = AssetValueType.None, Children = [name] };
        var field = new AssetTypeValueField
        {
            TemplateField = root,
            Children = [new AssetTypeValueField { TemplateField = name, Value = new AssetTypeValue(AssetValueType.String, "Rex's café <1> & 2+2"), Children = [] }],
        };

        var json = FieldJsonWriter.ToJson(field);

        Assert.Contains("Rex's café <1> & 2+2", json);
    }

    [Fact]
    public void The_workspace_file_keeps_an_apostrophe_in_the_game_path()
    {
        using var game = new FakeGame(folderName: "Rex's Kingdom");
        var dir = Path.Combine(Path.GetTempPath(), "tyrant-tests", Guid.NewGuid().ToString("N"), "ws");

        Workspace.Create(dir, new GameInstall(game.Root, null));

        Assert.Contains("Rex's Kingdom", File.ReadAllText(Path.Combine(dir, Workspace.FileName)));
    }
}
