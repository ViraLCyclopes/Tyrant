using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Tyrant.Framework.Core
{
    /// <summary>
    /// A small JSON reader and writer for mod files, so the framework needs no JSON library inside the game.
    /// Objects are Dictionary&lt;string, object?&gt;, arrays List&lt;object?&gt;, numbers double; plus string, bool and null.
    /// </summary>
    public static class Json
    {
        public static object? Parse(string text)
        {
            var reader = new Reader(text);
            reader.SkipWhitespace();
            var value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd) throw reader.Error("unexpected text after the JSON value");
            return value;
        }

        public static string Write(object? value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object? value, int indent)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string s: WriteString(sb, s); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object?> map:
                    if (map.Count == 0) { sb.Append("{}"); break; }
                    sb.Append("{\n");
                    var firstPair = true;
                    foreach (var pair in map)
                    {
                        if (!firstPair) sb.Append(",\n");
                        firstPair = false;
                        sb.Append(' ', (indent + 1) * 2);
                        WriteString(sb, pair.Key);
                        sb.Append(": ");
                        WriteValue(sb, pair.Value, indent + 1);
                    }
                    sb.Append('\n').Append(' ', indent * 2).Append('}');
                    break;
                case IEnumerable list:
                    var items = new List<object?>();
                    foreach (var item in list) items.Add(item);
                    if (items.Count == 0) { sb.Append("[]"); break; }
                    sb.Append("[\n");
                    for (var k = 0; k < items.Count; k++)
                    {
                        if (k > 0) sb.Append(",\n");
                        sb.Append(' ', (indent + 1) * 2);
                        WriteValue(sb, items[k], indent + 1);
                    }
                    sb.Append('\n').Append(' ', indent * 2).Append(']');
                    break;
                default:
                    throw new ArgumentException("Cannot write " + value.GetType().Name + " as JSON.");
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class Reader
        {
            private readonly string _text;
            private int _pos;

            public Reader(string text) => _text = text;

            public bool AtEnd => _pos >= _text.Length;

            public void SkipWhitespace()
            {
                while (!AtEnd && char.IsWhiteSpace(_text[_pos])) _pos++;
            }

            public object? ReadValue()
            {
                if (AtEnd) throw Error("the text ended where a value was expected");
                switch (_text[_pos])
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ReadNumber();
                }
            }

            private Dictionary<string, object?> ReadObject()
            {
                var map = new Dictionary<string, object?>();
                _pos++; // {
                SkipWhitespace();
                if (Peek('}')) { _pos++; return map; }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _text[_pos] != '"') throw Error("a property name in quotes was expected");
                    var key = ReadString();
                    SkipWhitespace();
                    if (!Peek(':')) throw Error("':' was expected after \"" + key + "\"");
                    _pos++;
                    SkipWhitespace();
                    map[key] = ReadValue();
                    SkipWhitespace();
                    if (Peek(',')) { _pos++; continue; }
                    if (Peek('}')) { _pos++; return map; }
                    throw Error(AtEnd ? "the text ended inside an object" : "',' or '}' was expected");
                }
            }

            private List<object?> ReadArray()
            {
                var list = new List<object?>();
                _pos++; // [
                SkipWhitespace();
                if (Peek(']')) { _pos++; return list; }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ReadValue());
                    SkipWhitespace();
                    if (Peek(',')) { _pos++; continue; }
                    if (Peek(']')) { _pos++; return list; }
                    throw Error(AtEnd ? "the text ended inside an array" : "',' or ']' was expected");
                }
            }

            private string ReadString()
            {
                var sb = new StringBuilder();
                _pos++; // opening quote
                while (true)
                {
                    if (AtEnd) throw Error("the text ended inside a string");
                    var c = _text[_pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Error("the text ended inside a string");
                    var e = _text[_pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _text.Length
                                || !int.TryParse(_text.Substring(_pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                                throw Error("a \\u escape needs four hex digits");
                            sb.Append((char)code);
                            _pos += 4;
                            break;
                        default: throw Error("unknown escape \\" + e);
                    }
                }
            }

            private double ReadNumber()
            {
                var start = _pos;
                while (!AtEnd && "+-0123456789.eE".IndexOf(_text[_pos]) >= 0) _pos++;
                if (start == _pos
                    || !double.TryParse(_text.Substring(start, _pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    _pos = start;
                    throw Error("a value was expected");
                }
                return number;
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_text, _pos, word, 0, word.Length) != 0) throw Error("a value was expected");
                _pos += word.Length;
            }

            private bool Peek(char c) => !AtEnd && _text[_pos] == c;

            public FormatException Error(string message)
            {
                int line = 1, column = 1;
                for (var k = 0; k < _pos && k < _text.Length; k++)
                {
                    if (_text[k] == '\n') { line++; column = 1; }
                    else column++;
                }
                return new FormatException(AtEnd ? message + " (at the end)" : $"{message} (line {line}, column {column})");
            }
        }
    }
}
