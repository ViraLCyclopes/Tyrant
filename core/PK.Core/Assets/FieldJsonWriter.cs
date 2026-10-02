using System.Globalization;
using System.Text;
using System.Text.Json;
using AssetsTools.NET;

namespace PK.Core.Assets;

/// <summary>Writes a deserialized Unity object (AssetsTools.NET value tree) as readable JSON.</summary>
public static class FieldJsonWriter
{
    /// <summary>Primitive arrays longer than this are summarized instead of written out.</summary>
    public const int MaxInlinePrimitiveArray = 256;

    public static string ToJson(AssetTypeValueField field)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            Write(writer, field);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static void Write(Utf8JsonWriter writer, AssetTypeValueField field)
    {
        var valueType = field.Value?.ValueType ?? AssetValueType.None;
        if (valueType == AssetValueType.ByteArray)
        {
            writer.WriteStartObject();
            writer.WriteNumber("$bytes", field.AsByteArray?.Length ?? 0);
            writer.WriteEndObject();
            return;
        }
        if (valueType == AssetValueType.ManagedReferencesRegistry)
        {
            writer.WriteStringValue("$managedReferences");
            return;
        }
        if (field.TemplateField.IsArray)
        {
            WriteArray(writer, field);
            return;
        }
        var children = field.Children ?? [];
        if (children.Count == 1 && children[0].TemplateField.IsArray && children[0].FieldName == "Array")
        {
            WriteArray(writer, children[0]);
            return;
        }
        if (children.Count > 0)
        {
            writer.WriteStartObject();
            foreach (var child in children)
            {
                writer.WritePropertyName(child.FieldName);
                Write(writer, child);
            }
            writer.WriteEndObject();
            return;
        }
        WritePrimitive(writer, field, valueType);
    }

    private static void WriteArray(Utf8JsonWriter writer, AssetTypeValueField array)
    {
        var elements = array.Children ?? [];
        // Only numbers/bools are summarized (vertex/index buffers); strings are always written so no data is lost.
        if (elements.Count > MaxInlinePrimitiveArray
            && elements.All(e => (e.Children is null || e.Children.Count == 0) && e.Value?.ValueType != AssetValueType.String))
        {
            writer.WriteStartObject();
            writer.WriteString("$array", elements[0].TypeName);
            writer.WriteNumber("count", elements.Count);
            writer.WriteEndObject();
            return;
        }
        writer.WriteStartArray();
        foreach (var element in elements) Write(writer, element);
        writer.WriteEndArray();
    }

    private static void WritePrimitive(Utf8JsonWriter writer, AssetTypeValueField field, AssetValueType valueType)
    {
        switch (valueType)
        {
            case AssetValueType.Bool: writer.WriteBooleanValue(field.AsBool); break;
            case AssetValueType.Int8: writer.WriteNumberValue(field.AsSByte); break;
            case AssetValueType.UInt8: writer.WriteNumberValue(field.AsByte); break;
            case AssetValueType.Int16: writer.WriteNumberValue(field.AsShort); break;
            case AssetValueType.UInt16: writer.WriteNumberValue(field.AsUShort); break;
            case AssetValueType.Int32: writer.WriteNumberValue(field.AsInt); break;
            case AssetValueType.UInt32: writer.WriteNumberValue(field.AsUInt); break;
            case AssetValueType.Int64: writer.WriteNumberValue(field.AsLong); break;
            case AssetValueType.UInt64: writer.WriteNumberValue(field.AsULong); break;
            case AssetValueType.Float:
                var f = field.AsFloat;
                if (float.IsFinite(f)) writer.WriteNumberValue(f);
                else writer.WriteStringValue(f.ToString(CultureInfo.InvariantCulture));
                break;
            case AssetValueType.Double:
                var d = field.AsDouble;
                if (double.IsFinite(d)) writer.WriteNumberValue(d);
                else writer.WriteStringValue(d.ToString(CultureInfo.InvariantCulture));
                break;
            case AssetValueType.String: writer.WriteStringValue(field.AsString); break;
            default: writer.WriteNullValue(); break;
        }
    }
}
