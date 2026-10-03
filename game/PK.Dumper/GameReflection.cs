using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PK.Dumper.Serialization;
using UnityEngine;

namespace PK.Dumper
{
    /// <summary>Reflection lookups into the game (no compile-time dependency on Assembly-CSharp).</summary>
    internal static class GameReflection
    {
        public static UnityEngine.Object? FindPersistentData()
        {
            var type = FindType("PrehistoricKingdom.PKPersistentData");
            return type == null ? null : UnityEngine.Object.FindObjectOfType(type, true);
        }

        /// <summary>Every ScriptableObject (or array/list of them) PKPersistentData references through serialized fields.</summary>
        public static IEnumerable<object> DatabaseRoots(UnityEngine.Object persistent)
        {
            foreach (var field in persistent.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!field.IsPublic && !field.GetCustomAttributes(false).Any(a => a.GetType().Name == "SerializeField")) continue;
                var value = field.GetValue(persistent);
                if (value is ScriptableObject so && so != null)
                {
                    yield return so;
                }
                else if (value is IEnumerable sequence && !(value is string))
                {
                    foreach (var item in sequence)
                        if (item is ScriptableObject element && element != null) yield return element;
                }
            }
        }

        /// <summary>All I2 Localization languages and terms (sources merged by language code).</summary>
        public static List<LanguageTable> LocalizationTables(out string? error)
        {
            error = null;
            var tables = new Dictionary<string, LanguageTable>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var manager = FindType("I2.Loc.LocalizationManager");
                var sourcesField = manager?.GetField("Sources", BindingFlags.Static | BindingFlags.Public);
                var sources = sourcesField?.GetValue(null) as IList;
                if (sources != null && sources.Count == 0)
                {
                    // I2 loads its sources lazily (on first translation); ask it to load them now.
                    manager!.GetMethod("UpdateSources", BindingFlags.Static | BindingFlags.Public, null, Type.EmptyTypes, null)?.Invoke(null, null);
                    sources = sourcesField!.GetValue(null) as IList;
                }
                if (sources == null)
                {
                    error = "I2 Localization sources were not found.";
                    return new List<LanguageTable>();
                }
                foreach (var source in sources)
                {
                    var languages = (Field(source, "mLanguages") as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
                    var terms = (Field(source, "mTerms") as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
                    for (var i = 0; i < languages.Count; i++)
                    {
                        var code = Field(languages[i], "Code") as string;
                        var name = Field(languages[i], "Name") as string ?? code ?? "language" + i;
                        if (string.IsNullOrEmpty(code)) code = name;
                        if (!tables.TryGetValue(code!, out var table))
                            tables[code!] = table = new LanguageTable(code!, name, new Dictionary<string, string>(StringComparer.Ordinal));
                        foreach (var term in terms)
                        {
                            var key = Field(term, "Term") as string;
                            var texts = Field(term, "Languages") as string[];
                            if (!string.IsNullOrEmpty(key) && texts != null && i < texts.Length && texts[i] != null)
                                table.Terms[key!] = texts[i];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                error = "Reading I2 Localization failed: " + ex.Message;
            }
            if (error == null && tables.Count == 0)
                error = "I2 Localization had no loaded languages; game text is still dumped inside each PKLocalizationAsset.";
            return tables.Values.OrderBy(t => t.Code, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// The game cancels Application.Quit with a "Quit to desktop?" dialog unless InterfaceManager.quitConfirmed is set;
        /// setting it is what the dialog's own confirm button does. Returns false if the flag could not be set.
        /// </summary>
        public static bool ConfirmQuit()
        {
            try
            {
                var field = FindType("PrehistoricKingdom.InterfaceManager")?.GetField("quitConfirmed", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (field == null || field.FieldType != typeof(bool)) return false;
                field.SetValue(null, true);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static object? Field(object target, string name) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);

        private static Type? FindType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName, false)).FirstOrDefault(t => t != null);
    }
}
