using System;
using System.Collections.Generic;
using System.Linq;

namespace Tyrant.Framework.Core
{
    public sealed class AnimalText
    {
        public static readonly string[] Languages = { "English", "French", "Spanish", "Portuguese", "German", "Russian", "Chinese" };
        public static readonly string[] Keys = { "name", "description", "characterNotes", "socialInfo" };
        public string Language { get; set; } = "English";
        public string Key { get; set; } = "name";
        public string Text { get; set; } = "";
        public AnimalText Copy() => (AnimalText)MemberwiseClone();
        public void Validate()
        {
            if (!Languages.Contains(Language) || !Keys.Contains(Key)) throw new ManifestException("Unknown animal translation: " + Language + "/" + Key);
        }
        public static string? Resolve(IEnumerable<AnimalText> entries, string key, string language) =>
            entries.FirstOrDefault(t => t.Key == key && t.Language == language && !string.IsNullOrEmpty(t.Text))?.Text
            ?? entries.FirstOrDefault(t => t.Key == key && t.Language == "English" && !string.IsNullOrEmpty(t.Text))?.Text;
        internal Dictionary<string, object?> ToMap() => new Dictionary<string, object?>
        { ["language"] = Language, ["key"] = Key, ["text"] = Text };
    }
}
