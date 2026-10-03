using System.Collections.Generic;
using Tyrant.Framework.Core;

namespace Tyrant.Framework.Tests;

public class JsonTests
{
    [Fact]
    public void Parses_objects_arrays_strings_numbers_booleans_and_null()
    {
        var value = (Dictionary<string, object?>)Json.Parse("""{ "a": [1, 2.5, -3e2], "b": "x\"y\\z\u00e9\n", "c": true, "d": false, "e": null, "f": {} }""")!;

        Assert.Equal(new List<object?> { 1d, 2.5d, -300d }, value["a"]);
        Assert.Equal("x\"y\\zé\n", value["b"]);
        Assert.Equal(true, value["c"]);
        Assert.Equal(false, value["d"]);
        Assert.Null(value["e"]);
        Assert.Empty((Dictionary<string, object?>)value["f"]!);
    }

    [Theory]
    [InlineData("{ \"a\": 1, }", "line 1")]
    [InlineData("{\n  \"a\" 1 }", "line 2")]
    [InlineData("[1, 2", "end")]
    [InlineData("{} x", "after")]
    public void Invalid_json_says_where(string text, string where)
    {
        var ex = Assert.Throws<FormatException>(() => Json.Parse(text));

        Assert.Contains(where, ex.Message);
    }

    [Fact]
    public void Written_json_reads_back_the_same()
    {
        var value = new Dictionary<string, object?>
        {
            ["id"] = "red-spot", ["n"] = 3, ["list"] = new List<object?> { "a", true, null }, ["empty"] = new Dictionary<string, object?>(),
        };

        var text = Json.Write(value);
        var back = (Dictionary<string, object?>)Json.Parse(text)!;

        Assert.Equal("red-spot", back["id"]);
        Assert.Equal(3d, back["n"]);
        Assert.Equal(new List<object?> { "a", true, null }, back["list"]);
        Assert.Contains("\n  \"id\": \"red-spot\"", text); // indented, readable by people
    }
}
