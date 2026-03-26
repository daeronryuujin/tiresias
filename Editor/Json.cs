using System.Collections.Generic;
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
