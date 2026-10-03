using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PK.Dumper.Serialization
{
    /// <summary>Minimal indented JSON writer with no dependencies (safe to load inside the game).</summary>
    public sealed class JsonWriter
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly Stack<bool> _empty = new Stack<bool>();
        private bool _afterName;

        public void StartObject() { Prefix(); _sb.Append('{'); _empty.Push(true); }
        public void EndObject() { Close('}'); }
        public void StartArray() { Prefix(); _sb.Append('['); _empty.Push(true); }
        public void EndArray() { Close(']'); }

        public void Name(string name)
        {
            Prefix();
            WriteString(name);
            _sb.Append(": ");
            _afterName = true;
        }

        public void String(string? value)
        {
            if (value == null) { Null(); return; }
            Prefix();
            WriteString(value);
        }

        public void Number(long value) { Prefix(); _sb.Append(value.ToString(CultureInfo.InvariantCulture)); }
        public void Number(ulong value) { Prefix(); _sb.Append(value.ToString(CultureInfo.InvariantCulture)); }

        public void Number(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) { String(value.ToString(CultureInfo.InvariantCulture)); return; }
            Prefix();
            _sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        public void Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) { String(value.ToString(CultureInfo.InvariantCulture)); return; }
            Prefix();
            _sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        public void Bool(bool value) { Prefix(); _sb.Append(value ? "true" : "false"); }
        public void Null() { Prefix(); _sb.Append("null"); }

        public override string ToString() => _sb.ToString();

        private void Prefix()
        {
            if (_afterName) { _afterName = false; return; }
            if (_empty.Count == 0) return;
            if (!_empty.Pop()) _sb.Append(',');
            _empty.Push(false);
            NewLine(_empty.Count);
        }

        private void Close(char bracket)
        {
            var wasEmpty = _empty.Pop();
            if (!wasEmpty) NewLine(_empty.Count);
            _sb.Append(bracket);
        }

        private void NewLine(int depth)
        {
            _sb.Append('\n');
            _sb.Append(' ', depth * 2);
        }

        private void WriteString(string s)
        {
            _sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    case '\b': _sb.Append("\\b"); break;
                    case '\f': _sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) _sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else _sb.Append(c);
                        break;
                }
            }
            _sb.Append('"');
        }
    }
}
