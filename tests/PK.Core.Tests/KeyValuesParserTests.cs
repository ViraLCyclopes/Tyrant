using PK.Core.Install;

namespace PK.Core.Tests;

public class KeyValuesParserTests
{
    private const string LibraryFolders = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\Program Files (x86)\\Steam"
        		"label"		""
        		"apps"
        		{
        			"228980"		"123"
        		}
        	}
        	// secondary library
        	"1"
        	{
        		"path"		"E:\\SteamLibrary"
        	}
        }
        """;

    [Fact]
    public void Parses_nested_blocks_and_unescapes_backslashes()
    {
        var root = KeyValuesParser.Parse(LibraryFolders);
        var libs = root["libraryfolders"]!;
        Assert.Equal(2, libs.Children.Count);
        Assert.Equal(@"C:\Program Files (x86)\Steam", libs["0"]!["path"]!.Value);
        Assert.Equal(@"E:\SteamLibrary", libs["1"]!["path"]!.Value);
        Assert.Equal("123", libs["0"]!["apps"]!["228980"]!.Value);
    }

    [Fact]
    public void Indexer_is_case_insensitive_and_returns_null_when_missing()
    {
        var root = KeyValuesParser.Parse("\"AppState\" { \"installdir\" \"Prehistoric Kingdom\" }");
        Assert.Equal("Prehistoric Kingdom", root["appstate"]!["InstallDir"]!.Value);
        Assert.Null(root["AppState"]!["appid"]);
    }

    [Fact]
    public void Empty_string_value_is_kept()
    {
        var root = KeyValuesParser.Parse("\"label\" \"\"");
        Assert.Equal("", root["label"]!.Value);
    }

    [Theory]
    [InlineData("\"a\" { \"b\" \"c\"")]      // missing close brace
    [InlineData("\"a\" \"unterminated")]      // unterminated string
    [InlineData("}")]                         // stray close
    [InlineData("\"lonely\"")]                // key without value
    public void Malformed_input_throws_FormatException(string text)
    {
        Assert.Throws<FormatException>(() => KeyValuesParser.Parse(text));
    }
}
