using System;

namespace Tyrant.Dumper.Serialization
{
    /// <summary>I2 Localization lists its "Description" column among the languages; it is not one.</summary>
    public static class LanguageFilter
    {
        public static bool IsLanguage(string? code, string? name)
        {
            if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(name)) return false;
            return !(string.IsNullOrEmpty(code) && string.Equals(name, "Description", StringComparison.OrdinalIgnoreCase));
        }
    }
}
