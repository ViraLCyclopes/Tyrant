using System.Text;

namespace Tyrant.Core.Install;

/// <summary>One KeyValues entry: either a string value or a block of children.</summary>
public sealed class KvNode(string key, string? value)
{
    private readonly List<KvNode> _children = [];

    public string Key { get; } = key;
    public string? Value { get; } = value;
    public IReadOnlyList<KvNode> Children => _children;

    public KvNode? this[string key] =>
        _children.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

    internal void Add(KvNode child) => _children.Add(child);
}

/// <summary>Parser for Valve's KeyValues text format (libraryfolders.vdf, appmanifest_*.acf).</summary>
public static class KeyValuesParser
{
    private enum TokKind { String, Open, Close }

    private readonly record struct Tok(TokKind Kind, string Text);

    public static KvNode Parse(string text)
    {
        var toks = Tokenize(text);
        var pos = 0;
        var root = new KvNode("", null);
        ParseBody(toks, ref pos, root, topLevel: true);
        return root;
    }

    private static void ParseBody(List<Tok> t, ref int pos, KvNode parent, bool topLevel)
    {
        while (pos < t.Count)
        {
            var tok = t[pos];
            if (tok.Kind == TokKind.Close)
            {
                if (topLevel) throw new FormatException("Unexpected '}' in KeyValues text.");
                pos++;
                return;
            }
            if (tok.Kind == TokKind.Open) throw new FormatException("Unexpected '{' in KeyValues text.");

            pos++;
            if (pos >= t.Count) throw new FormatException($"Key '{tok.Text}' has no value.");
            var next = t[pos];
            if (next.Kind == TokKind.Open)
            {
                pos++;
                var node = new KvNode(tok.Text, null);
                ParseBody(t, ref pos, node, topLevel: false);
                parent.Add(node);
            }
            else if (next.Kind == TokKind.String)
            {
                pos++;
                parent.Add(new KvNode(tok.Text, next.Text));
            }
            else
            {
                throw new FormatException($"Key '{tok.Text}' has no value.");
            }
        }
        if (!topLevel) throw new FormatException("Missing '}' in KeyValues text.");
    }

    private static List<Tok> Tokenize(string s)
    {
        var list = new List<Tok>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }
            if (c == '{') { list.Add(new Tok(TokKind.Open, "{")); i++; continue; }
            if (c == '}') { list.Add(new Tok(TokKind.Close, "}")); i++; continue; }
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        var n = s[i + 1];
                        sb.Append(n switch { 'n' => '\n', 't' => '\t', _ => n });
                        i += 2;
                    }
                    else
                    {
                        sb.Append(s[i++]);
                    }
                }
                if (i >= s.Length) throw new FormatException("Unterminated string in KeyValues text.");
                i++; // closing quote
                list.Add(new Tok(TokKind.String, sb.ToString()));
                continue;
            }
            var start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] is not ('{' or '}' or '"')) i++;
            list.Add(new Tok(TokKind.String, s[start..i]));
        }
        return list;
    }
}
