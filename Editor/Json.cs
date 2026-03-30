using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Tiresias
{
    /// <summary>
    /// Dead-simple JSON builder. No external deps, no reflection magic.
    /// Just enough to hand-craft the responses we need.
    /// </summary>
    public static class Json
    {
        public static string Quote(string s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
        }

        public static string Array(IEnumerable<string> items)
            => "[" + string.Join(",", items) + "]";

        /// <summary>
        /// Builds a JSON object from a dictionary.
        /// Values can be: string (gets quoted), bool, int, float, double, or a raw JSON string
        /// (pre-built arrays/objects — wrap in RawJson to skip quoting).
        /// </summary>
        public static string Object(Dictionary<string, object> fields)
        {
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var kv in fields)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Quote(kv.Key));
                sb.Append(':');
                sb.Append(Serialize(kv.Value));
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static string Serialize(object value)
        {
            if (value == null)         return "null";
            if (value is bool b)       return b ? "true" : "false";
            if (value is int i)        return i.ToString();
            if (value is long l)       return l.ToString();
            if (value is float f)      return f.ToString("G");
            if (value is double d)     return d.ToString("G");
            if (value is RawJson raw)  return raw.Value;
            return Quote(value.ToString());
        }

        // ── Deserialization (minimal, for request bodies) ──────────────────

        /// <summary>
        /// Read the full request body as a UTF-8 string.
        /// </summary>
        public static string ReadBody(HttpListenerRequest req)
        {
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        /// <summary>
        /// Parse a flat JSON object where all values are strings.
        /// e.g. {"key":"value","key2":"value2"} → Dictionary.
        /// Returns empty dictionary on null/empty/malformed input.
        /// </summary>
        public static Dictionary<string, string> ParseFlat(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json)) return result;

            json = json.Trim();
            if (json.Length < 2 || json[0] != '{' || json[json.Length - 1] != '}')
                return result;

            // Strip outer braces
            json = json.Substring(1, json.Length - 2).Trim();
            if (json.Length == 0) return result;

            int i = 0;
            while (i < json.Length)
            {
                // Find key
                var key = ParseQuotedString(json, ref i);
                if (key == null) break;

                SkipWhitespace(json, ref i);
                if (i >= json.Length || json[i] != ':') break;
                i++; // skip ':'
                SkipWhitespace(json, ref i);

                // Find value
                var value = ParseQuotedString(json, ref i);
                if (value == null) break;

                result[key] = value;

                SkipWhitespace(json, ref i);
                if (i < json.Length && json[i] == ',') i++;
                SkipWhitespace(json, ref i);
            }

            return result;
        }

        private static string ParseQuotedString(string json, ref int i)
        {
            SkipWhitespace(json, ref i);
            if (i >= json.Length || json[i] != '"') return null;
            i++; // skip opening quote

            var sb = new StringBuilder();
            while (i < json.Length)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    if (next == '"' || next == '\\' || next == '/')
                    {
                        sb.Append(next);
                        i += 2;
                    }
                    else if (next == 'n') { sb.Append('\n'); i += 2; }
                    else if (next == 'r') { sb.Append('\r'); i += 2; }
                    else if (next == 't') { sb.Append('\t'); i += 2; }
                    else { sb.Append(c); i++; }
                }
                else if (c == '"')
                {
                    i++; // skip closing quote
                    return sb.ToString();
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            return null; // unterminated string
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        /// <summary>
        /// Parse a JSON array of {method, path} objects for the /batch endpoint.
        /// e.g. [{"method":"GET","path":"/status"},{"method":"GET","path":"/compiler/errors"}]
        /// </summary>
        public static List<(string method, string path)> ParseBatchRequests(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            json = json.Trim();
            if (json.Length < 2 || json[0] != '[') return null;

            var results = new List<(string, string)>();
            int i = 1; // skip '['

            while (i < json.Length)
            {
                SkipWhitespace(json, ref i);
                if (i >= json.Length || json[i] == ']') break;

                if (json[i] == '{')
                {
                    // Find matching close brace
                    int depth = 0;
                    int objStart = i;
                    while (i < json.Length)
                    {
                        if (json[i] == '{') depth++;
                        else if (json[i] == '}') { depth--; if (depth == 0) { i++; break; } }
                        i++;
                    }
                    var objStr = json.Substring(objStart, i - objStart);
                    var fields = ParseFlat(objStr);
                    fields.TryGetValue("method", out var method);
                    fields.TryGetValue("path", out var path);
                    if (!string.IsNullOrEmpty(path))
                        results.Add((method ?? "GET", path));
                }
                else
                {
                    i++;
                }

                SkipWhitespace(json, ref i);
                if (i < json.Length && json[i] == ',') i++;
            }

            return results;
        }
    }

    /// <summary>
    /// Wrap a pre-serialized JSON string so Json.Object won't re-quote it.
    /// </summary>
    public class RawJson
    {
        public readonly string Value;
        public RawJson(string v) { Value = v; }
    }
}
