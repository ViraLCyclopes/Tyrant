using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tyrant.Framework.Core
{
    /// <summary>A leaf of the game's serialized animal asset graph. Values are plain SQL-editable text.</summary>
    public sealed class AnimalField
    {
        public string Path { get; set; } = "";
        public string Kind { get; set; } = "text";
        public string Value { get; set; } = "";
        public string TemplateValue { get; set; } = "";
        public bool Editable { get; set; }
        public AnimalField Copy() => (AnimalField)MemberwiseClone();
        public void Validate()
        {
            if (!Path.StartsWith("/", StringComparison.Ordinal) || Path.Contains("//")) throw new ManifestException("Invalid animal field path: " + Path);
            if (Kind == "number")
            {
                if (!double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || double.IsInfinity(n) || double.IsNaN(n))
                    throw new ManifestException(Path + ": expected a finite number.");
            }
            else if (Kind == "boolean")
            {
                if (Value != "true" && Value != "false") throw new ManifestException(Path + ": expected true or false.");
            }
            else if (Kind != "text" && Kind != "reference" && Kind != "null" && Kind != "array" && Kind != "object")
                throw new ManifestException(Path + ": unknown field kind " + Kind);
            if (!Editable && Value != TemplateValue) throw new ManifestException(Path + ": structural/reference field is read-only.");
        }
        internal Dictionary<string, object?> ToMap() => new Dictionary<string, object?>
        { ["path"] = Path, ["kind"] = Kind, ["value"] = Value, ["template"] = TemplateValue, ["editable"] = Editable };
    }
}
