using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PK.Dumper.Serialization
{
    public sealed class LanguageTable
    {
        public LanguageTable(string code, string name, Dictionary<string, string> terms)
        {
            Code = code;
            Name = name;
            Terms = terms;
        }

        public string Code { get; }
        public string Name { get; }
        public Dictionary<string, string> Terms { get; }
    }

    public sealed class DumpManifest
    {
        public string RequestId { get; set; } = "";
        public string BuildGuid { get; set; } = "";
        public string DumperVersion { get; set; } = "";
        public string CreatedUtc { get; set; } = "";
        public Dictionary<string, int> Counts { get; } = new Dictionary<string, int>();
        public List<string> Languages { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();

        public string ToJson()
        {
            var w = new JsonWriter();
            w.StartObject();
            w.Name("schemaVersion"); w.Number(1L);
            w.Name("requestId"); w.String(RequestId);
            w.Name("buildGuid"); w.String(BuildGuid);
            w.Name("dumperVersion"); w.String(DumperVersion);
            w.Name("createdUtc"); w.String(CreatedUtc);
            w.Name("counts"); w.StartObject();
            foreach (var kv in Counts.OrderBy(k => k.Key, StringComparer.Ordinal)) { w.Name(kv.Key); w.Number((long)kv.Value); }
            w.EndObject();
            w.Name("languages"); w.StartArray();
            foreach (var l in Languages) w.String(l);
            w.EndArray();
            w.Name("errors"); w.StartArray();
            foreach (var e in Errors) w.String(e);
            w.EndArray();
            w.EndObject();
            return w.ToString();
        }
    }

    /// <summary>Writes a dump folder; manifest.json is written last and marks the dump complete.</summary>
    public static class DumpWriter
    {
        public const string ManifestFile = "manifest.json";

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly char[] Invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).Distinct().ToArray();

        public static void Write(string outputDir, DumpResult result, IEnumerable<LanguageTable> languages, DumpManifest manifest)
        {
            Directory.CreateDirectory(outputDir);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var types = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var obj in result.Objects)
            {
                var typeDir = Path.Combine(outputDir, "objects", SafeName(obj.Type));
                Directory.CreateDirectory(typeDir);
                var baseName = SafeName(obj.Name.Length > 0 ? obj.Name : "unnamed");
                var file = Path.Combine(typeDir, baseName + ".json");
                if (!used.Add(file))
                {
                    file = Path.Combine(typeDir, baseName + "_" + obj.Id + ".json");
                    for (var n = 2; !used.Add(file); n++) file = Path.Combine(typeDir, baseName + "_" + obj.Id + "_" + n + ".json");
                }
                File.WriteAllText(file, obj.Json, Utf8);
                manifest.Counts[obj.Type] = manifest.Counts.TryGetValue(obj.Type, out var count) ? count + 1 : 1;
                types[obj.Type] = obj.ClrType;
            }

            var schemaDir = Path.Combine(outputDir, "schema");
            Directory.CreateDirectory(schemaDir);
            foreach (var kv in types) File.WriteAllText(Path.Combine(schemaDir, SafeName(kv.Key) + ".json"), Schema(kv.Value), Utf8);

            var localizationDir = Path.Combine(outputDir, "localization");
            foreach (var language in languages)
            {
                Directory.CreateDirectory(localizationDir);
                var w = new JsonWriter();
                w.StartObject();
                w.Name("code"); w.String(language.Code);
                w.Name("name"); w.String(language.Name);
                w.Name("terms"); w.StartObject();
                foreach (var term in language.Terms.OrderBy(t => t.Key, StringComparer.Ordinal)) { w.Name(term.Key); w.String(term.Value); }
                w.EndObject();
                w.EndObject();
                File.WriteAllText(Path.Combine(localizationDir, SafeName(language.Code) + ".json"), w.ToString(), Utf8);
                manifest.Languages.Add(language.Code);
            }

            manifest.Errors.AddRange(result.Errors);
            File.WriteAllText(Path.Combine(outputDir, ManifestFile), manifest.ToJson(), Utf8);
        }

        public static string SafeName(string text)
        {
            var chars = text.Select(c => Invalid.Contains(c) || c < 32 ? '_' : c).ToArray();
            var cleaned = new string(chars).Trim().TrimEnd('.');
            if (cleaned.Length == 0 || cleaned.All(c => c == '.')) return "_";
            return cleaned.Length > 120 ? cleaned.Substring(0, 120) : cleaned;
        }

        private static string Schema(Type type)
        {
            var w = new JsonWriter();
            w.StartObject();
            w.Name("type"); w.String(type.FullName);
            w.Name("fields"); w.StartArray();
            foreach (var field in DumpSerializer.SerializedFields(type))
            {
                w.StartObject();
                w.Name("name"); w.String(DumpSerializer.JsonName(field));
                w.Name("type"); w.String(field.FieldType.FullName ?? field.FieldType.Name);
                var enumType = field.FieldType.IsEnum ? field.FieldType : Nullable.GetUnderlyingType(field.FieldType);
                if (enumType != null && enumType.IsEnum)
                {
                    w.Name("enum"); w.StartArray();
                    foreach (var name in Enum.GetNames(enumType)) w.String(name);
                    w.EndArray();
                }
                w.EndObject();
            }
            w.EndArray();
            w.EndObject();
            return w.ToString();
        }
    }
}
