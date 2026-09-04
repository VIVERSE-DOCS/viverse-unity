using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace ViverseSDK
{
    /// <summary>
    /// Minimal JSON serializer/deserializer. No external dependencies.
    /// Handles Dictionary, List, string, number, bool, null.
    /// </summary>
    public static class MiniJson
    {
        public static object Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int index = 0;
            return ParseValue(json, ref index);
        }

        public static string Serialize(object obj)
        {
            var sb = new StringBuilder();
            SerializeValue(obj, sb);
            return sb.ToString();
        }

        private static object ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) return null;

            char c = json[index];
            if (c == '{') return ParseObject(json, ref index);
            if (c == '[') return ParseArray(json, ref index);
            if (c == '"') return ParseString(json, ref index);
            if (c == 't' || c == 'f') return ParseBool(json, ref index);
            if (c == 'n') return ParseNull(json, ref index);
            return ParseNumber(json, ref index);
        }

        private static Dictionary<string, object> ParseObject(string json, ref int index)
        {
            var dict = new Dictionary<string, object>();
            index++; // skip {
            SkipWhitespace(json, ref index);

            if (index < json.Length && json[index] == '}') { index++; return dict; }

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                string key = ParseString(json, ref index);
                SkipWhitespace(json, ref index);
                index++; // skip :
                object value = ParseValue(json, ref index);
                dict[key] = value;
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',') { index++; continue; }
                if (index < json.Length && json[index] == '}') { index++; break; }
                break;
            }
            return dict;
        }

        private static List<object> ParseArray(string json, ref int index)
        {
            var list = new List<object>();
            index++; // skip [
            SkipWhitespace(json, ref index);

            if (index < json.Length && json[index] == ']') { index++; return list; }

            while (index < json.Length)
            {
                list.Add(ParseValue(json, ref index));
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',') { index++; continue; }
                if (index < json.Length && json[index] == ']') { index++; break; }
                break;
            }
            return list;
        }

        private static string ParseString(string json, ref int index)
        {
            index++; // skip opening "
            var sb = new StringBuilder();
            while (index < json.Length)
            {
                char c = json[index];
                if (c == '\\')
                {
                    index++;
                    if (index >= json.Length) break;
                    char esc = json[index];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (index + 4 < json.Length)
                            {
                                string hex = json.Substring(index + 1, 4);
                                sb.Append((char)Convert.ToInt32(hex, 16));
                                index += 4;
                            }
                            break;
                        default: sb.Append(esc); break;
                    }
                }
                else if (c == '"')
                {
                    index++;
                    return sb.ToString();
                }
                else
                {
                    sb.Append(c);
                }
                index++;
            }
            return sb.ToString();
        }

        private static object ParseNumber(string json, ref int index)
        {
            int start = index;
            while (index < json.Length && "0123456789+-.eE".IndexOf(json[index]) >= 0) index++;
            string num = json.Substring(start, index - start);
            if (num.Contains(".") || num.Contains("e") || num.Contains("E"))
                return double.Parse(num, System.Globalization.CultureInfo.InvariantCulture);
            long l = long.Parse(num);
            if (l >= int.MinValue && l <= int.MaxValue) return (int)l;
            return l;
        }

        private static bool ParseBool(string json, ref int index)
        {
            if (json.Substring(index, 4) == "true") { index += 4; return true; }
            index += 5; return false;
        }

        private static object ParseNull(string json, ref int index)
        {
            index += 4;
            return null;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && " \t\n\r".IndexOf(json[index]) >= 0) index++;
        }

        private static void SerializeValue(object obj, StringBuilder sb)
        {
            if (obj == null) { sb.Append("null"); return; }
            if (obj is string s) { SerializeString(s, sb); return; }
            if (obj is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (obj is int || obj is long || obj is float || obj is double)
            {
                sb.Append(Convert.ToString(obj, System.Globalization.CultureInfo.InvariantCulture));
                return;
            }
            if (obj is IDictionary dict)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry entry in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    SerializeString(entry.Key.ToString(), sb);
                    sb.Append(':');
                    SerializeValue(entry.Value, sb);
                }
                sb.Append('}');
                return;
            }
            if (obj is IList list)
            {
                sb.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    SerializeValue(list[i], sb);
                }
                sb.Append(']');
                return;
            }
            // Fallback: ToString
            SerializeString(obj.ToString(), sb);
        }

        private static void SerializeString(string s, StringBuilder sb)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
