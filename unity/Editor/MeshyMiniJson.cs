using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FISHHWB.MeshyImporter.Editor
{
    /// <summary>
    /// Minimal, dependency-free JSON reader. Returns a plain object graph:
    /// Dictionary&lt;string, object&gt; for objects, List&lt;object&gt; for arrays,
    /// string, double, bool, or null for scalars. Written specifically to read
    /// glTF JSON so the native importer does not need Newtonsoft.Json or any
    /// other package.
    /// </summary>
    internal static class MeshyMiniJson
    {
        public static object Parse(string json)
        {
            int i = 0;
            SkipWhitespace(json, ref i);
            object result = ParseValue(json, ref i);
            SkipWhitespace(json, ref i);
            return result;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON.");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't':
                    Expect(s, ref i, "true");
                    return true;
                case 'f':
                    Expect(s, ref i, "false");
                    return false;
                case 'n':
                    Expect(s, ref i, "null");
                    return null;
                default:
                    return ParseNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var dict = new Dictionary<string, object>();
            i++; // {
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return dict; }
            while (true)
            {
                SkipWhitespace(s, ref i);
                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (s[i] != ':') throw new FormatException("Expected ':' in JSON object.");
                i++;
                object value = ParseValue(s, ref i);
                dict[key] = value;
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("Unterminated JSON object.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; break; }
                throw new FormatException("Expected ',' or '}' in JSON object.");
            }
            return dict;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // [
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                object value = ParseValue(s, ref i);
                list.Add(value);
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("Unterminated JSON array.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; break; }
                throw new FormatException("Expected ',' or ']' in JSON array.");
            }
            return list;
        }

        private static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("Expected '\"' to start JSON string.");
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("Unterminated JSON string.");
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\')
                {
                    char esc = s[i++];
                    switch (esc)
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
                            string hex = s.Substring(i, 4);
                            i += 4;
                            sb.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            break;
                        default: throw new FormatException("Unknown escape sequence in JSON string.");
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) i++;
            string num = s.Substring(start, i - start);
            return double.Parse(num, CultureInfo.InvariantCulture);
        }

        private static void Expect(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || s.Substring(i, literal.Length) != literal)
                throw new FormatException($"Expected '{literal}' in JSON.");
            i += literal.Length;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        // ---- Convenience accessors over the parsed object graph -----------

        public static Dictionary<string, object> AsObject(object o) => o as Dictionary<string, object>;
        public static List<object> AsArray(object o) => o as List<object>;
        public static string AsString(object o, string fallback = null) => o is string s ? s : fallback;
        public static bool AsBool(object o, bool fallback = false) => o is bool b ? b : fallback;

        public static double AsNumber(object o, double fallback = 0)
        {
            if (o is double d) return d;
            return fallback;
        }

        public static int AsInt(object o, int fallback = 0)
        {
            if (o is double d) return (int)d;
            return fallback;
        }

        public static Dictionary<string, object> Get(Dictionary<string, object> obj, string key)
        {
            if (obj == null) return null;
            return obj.TryGetValue(key, out var v) ? AsObject(v) : null;
        }

        public static List<object> GetArray(Dictionary<string, object> obj, string key)
        {
            if (obj == null) return null;
            return obj.TryGetValue(key, out var v) ? AsArray(v) : null;
        }

        public static string GetString(Dictionary<string, object> obj, string key, string fallback = null)
        {
            if (obj == null || !obj.TryGetValue(key, out var v)) return fallback;
            return AsString(v, fallback);
        }

        public static double GetNumber(Dictionary<string, object> obj, string key, double fallback = 0)
        {
            if (obj == null || !obj.TryGetValue(key, out var v)) return fallback;
            return AsNumber(v, fallback);
        }

        public static int GetInt(Dictionary<string, object> obj, string key, int fallback = 0)
        {
            if (obj == null || !obj.TryGetValue(key, out var v)) return fallback;
            return AsInt(v, fallback);
        }

        public static bool GetBool(Dictionary<string, object> obj, string key, bool fallback = false)
        {
            if (obj == null || !obj.TryGetValue(key, out var v)) return fallback;
            return AsBool(v, fallback);
        }

        public static bool Has(Dictionary<string, object> obj, string key) => obj != null && obj.ContainsKey(key);
    }
}
