using System.Text.Json;
using AssetsTools.NET;
using PK.Core.Assets;

namespace PK.Core.Tests;

public class FieldJsonWriterTests
{
    private static AssetTypeValueField Prim(string name, string type, AssetValueType valueType, object value) => new()
    {
        TemplateField = new AssetTypeTemplateField { Name = name, Type = type, ValueType = valueType, HasValue = true, Children = [] },
        Value = value is string text ? new AssetTypeValue(text) : new AssetTypeValue(valueType, value),
        Children = [],
    };

    private static AssetTypeValueField Obj(string name, string type, params AssetTypeValueField[] children) => new()
    {
        TemplateField = new AssetTypeTemplateField
        {
            Name = name, Type = type, ValueType = AssetValueType.None, Children = children.Select(c => c.TemplateField).ToList(),
        },
        Children = children.ToList(),
    };

    private static AssetTypeValueField Arr(string name, params AssetTypeValueField[] elements) => new()
    {
        TemplateField = new AssetTypeTemplateField { Name = name, Type = "Array", ValueType = AssetValueType.Array, IsArray = true, Children = [] },
        Value = new AssetTypeValue(AssetValueType.Array, new AssetTypeArrayInfo(elements.Length)),
        Children = elements.ToList(),
    };

    private static JsonElement Parse(AssetTypeValueField field) => JsonDocument.Parse(FieldJsonWriter.ToJson(field)).RootElement;

    [Fact]
    public void Writes_objects_and_primitives()
    {
        var json = Parse(Obj("Base", "AnimalData",
            Prim("m_Name", "string", AssetValueType.String, "Stegosaurus"),
            Prim("cost", "int", AssetValueType.Int32, 1200),
            Prim("scale", "float", AssetValueType.Float, 0.5f),
            Prim("showInGUI", "bool", AssetValueType.Bool, true),
            Obj("m_Script", "PPtr<MonoScript>", Prim("m_FileID", "int", AssetValueType.Int32, 0), Prim("m_PathID", "SInt64", AssetValueType.Int64, 123L))));

        Assert.Equal("Stegosaurus", json.GetProperty("m_Name").GetString());
        Assert.Equal(1200, json.GetProperty("cost").GetInt32());
        Assert.Equal(0.5, json.GetProperty("scale").GetDouble());
        Assert.True(json.GetProperty("showInGUI").GetBoolean());
        Assert.Equal(123, json.GetProperty("m_Script").GetProperty("m_PathID").GetInt64());
    }

    [Fact]
    public void Vector_wrapper_collapses_to_array()
    {
        var json = Parse(Obj("Base", "X",
            Obj("excavations", "vector", Arr("Array", Prim("data", "int", AssetValueType.Int32, 1), Prim("data", "int", AssetValueType.Int32, 2)))));

        Assert.Equal(new[] { 1, 2 }, json.GetProperty("excavations").EnumerateArray().Select(e => e.GetInt32()));
    }

    [Fact]
    public void Long_primitive_arrays_are_summarized()
    {
        var elements = Enumerable.Range(0, 300).Select(i => Prim("data", "int", AssetValueType.Int32, i)).ToArray();
        var json = Parse(Obj("Base", "X", Obj("m_Indices", "vector", Arr("Array", elements))));

        var summary = json.GetProperty("m_Indices");
        Assert.Equal("int", summary.GetProperty("$array").GetString());
        Assert.Equal(300, summary.GetProperty("count").GetInt32());
    }

    [Fact]
    public void Byte_arrays_are_summarized_by_length()
    {
        var bytes = new AssetTypeValueField
        {
            TemplateField = new AssetTypeTemplateField { Name = "image data", Type = "TypelessData", ValueType = AssetValueType.ByteArray, Children = [] },
            Value = new AssetTypeValue(new byte[] { 1, 2, 3 }, false),
            Children = [],
        };

        Assert.Equal(3, Parse(Obj("Base", "Texture2D", bytes)).GetProperty("image data").GetProperty("$bytes").GetInt32());
    }

    [Fact]
    public void Non_finite_floats_become_strings()
    {
        var json = Parse(Obj("Base", "X", Prim("v", "float", AssetValueType.Float, float.NaN), Prim("w", "double", AssetValueType.Double, double.PositiveInfinity)));
        Assert.Equal("NaN", json.GetProperty("v").GetString());
        Assert.Equal("Infinity", json.GetProperty("w").GetString());
    }
}
