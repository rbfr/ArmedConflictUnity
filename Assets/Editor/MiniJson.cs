using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Minimal JSON reader for the data import. Unity's JsonUtility maps onto typed fields and
/// cannot represent the arbitrary nested maps the Kotlin exporter emits, so this returns plain
/// Dictionary&lt;string,object&gt; / List&lt;object&gt; / string / double / bool / null.
/// </summary>
public static class MiniJson
{
    public static object Parse(string json)
    {
        int i = 0;
        var v = ParseValue(json, ref i);
        return v;
    }

    static void SkipWs(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    static object ParseValue(string s, ref int i)
    {
        SkipWs(s, ref i);
        if (i >= s.Length) return null;
        switch (s[i])
        {
            case '{': return ParseObject(s, ref i);
            case '[': return ParseArray(s, ref i);
            case '"': return ParseString(s, ref i);
            case 't': i += 4; return true;
            case 'f': i += 5; return false;
            case 'n': i += 4; return null;
            default: return ParseNumber(s, ref i);
        }
    }

    static Dictionary<string, object> ParseObject(string s, ref int i)
    {
        var d = new Dictionary<string, object>();
        i++;                                  // {
        while (true)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) break;
            if (s[i] == '}') { i++; break; }
            if (s[i] == ',') { i++; continue; }
            string key = ParseString(s, ref i);
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ':') i++;
            d[key] = ParseValue(s, ref i);
        }
        return d;
    }

    static List<object> ParseArray(string s, ref int i)
    {
        var l = new List<object>();
        i++;                                  // [
        while (true)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) break;
            if (s[i] == ']') { i++; break; }
            if (s[i] == ',') { i++; continue; }
            l.Add(ParseValue(s, ref i));
        }
        return l;
    }

    static string ParseString(string s, ref int i)
    {
        SkipWs(s, ref i);
        if (i >= s.Length || s[i] != '"') return null;
        i++;
        var sb = new StringBuilder();
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"') break;
            if (c != '\\') { sb.Append(c); continue; }
            char e = s[i++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u':
                    sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber));
                    i += 4;
                    break;
                default: sb.Append(e); break;
            }
        }
        return sb.ToString();
    }

    static object ParseNumber(string s, ref int i)
    {
        int start = i;
        while (i < s.Length && (char.IsDigit(s[i]) || "-+.eE".IndexOf(s[i]) >= 0)) i++;
        var text = s.Substring(start, i - start);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d : 0d;
    }
}
