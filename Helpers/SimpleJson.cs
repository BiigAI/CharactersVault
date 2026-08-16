using System;
using System.CodeDom;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace CharacterVault.Helpers
{
    /// <summary>
    /// A self-contained, zero-dependency JSON serializer and parser.
    /// Eliminates external NuGet references so the mod runs on any client or server
    /// without extra DLL requirements.
    /// </summary>
    public static class SimpleJson
    {
        public static string SerializeObject(object? obj, bool prettyPrint = true)
        {
            var sb = new StringBuilder();
            SerializeValue(obj, sb, prettyPrint ? 0 : -1);
            return sb.ToString();
        }

        public static T? DeserializeObject<T>(string json) where T : new()
        {
            if (string.IsNullOrWhiteSpace(json)) return default;

            object? parsed = Deserialize(json);
            if (parsed == null) return default;

            if (parsed is T direct) return direct;

            if (typeof(IDictionary).IsAssignableFrom(typeof(T)) && parsed is IDictionary<string, object?> dict)
            {
                Type[] genArgs = typeof(T).GetGenericArguments();
                if (genArgs.Length == 2 && genArgs[0] == typeof(string))
                {
                    Type valType = genArgs[1];
                    var resultDict = (IDictionary)Activator.CreateInstance(typeof(T))!;
                    foreach (var kvp in dict)
                    {
                        if (kvp.Value is IDictionary<string, object?> childDict)
                        {
                            object childObj = ConvertDictionaryToObject(childDict, valType);
                            resultDict.Add(kvp.Key, childObj);
                        }
                        else
                        {
                            resultDict.Add(kvp.Key, ConvertValue(kvp.Value, valType));
                        }
                    }
                    return (T)resultDict;
                }
            }

            if (parsed is IDictionary<string, object?> objDict)
            {
                return (T)ConvertDictionaryToObject(objDict, typeof(T));
            }

            return default;
        }

        private static object ConvertDictionaryToObject(IDictionary<string, object?> dict, Type targetType)
        {
            object instance = Activator.CreateInstance(targetType)!;
            PropertyInfo[] properties = targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            FieldInfo[] fields = targetType.GetFields(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                if (!prop.CanWrite) continue;
                if (FindKey(dict, prop.Name, out object? val))
                {
                    prop.SetValue(instance, ConvertValue(val, prop.PropertyType), null);
                }
            }

            foreach (var field in fields)
            {
                if (FindKey(dict, field.Name, out object? val))
                {
                    field.SetValue(instance, ConvertValue(val, field.FieldType));
                }
            }

            return instance;
        }

        private static bool FindKey(IDictionary<string, object?> dict, string name, out object? value)
        {
            if (dict.TryGetValue(name, out value)) return true;

            foreach (var kvp in dict)
            {
                if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = kvp.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static object? ConvertValue(object? val, Type targetType)
        {
            if (val == null) return null;
            if (targetType.IsAssignableFrom(val.GetType())) return val;

            if (targetType == typeof(DateTime))
            {
                if (val is string str && DateTime.TryParse(str, null, DateTimeStyles.RoundtripKind, out DateTime dt))
                {
                    return dt;
                }
                return default(DateTime);
            }

            if (targetType == typeof(bool))
            {
                if (val is bool b) return b;
                if (val is string s && bool.TryParse(s, out bool pb)) return pb;
                return false;
            }

            if (targetType == typeof(int)) return Convert.ToInt32(val, CultureInfo.InvariantCulture);
            if (targetType == typeof(long)) return Convert.ToInt64(val, CultureInfo.InvariantCulture);
            if (targetType == typeof(float)) return Convert.ToSingle(val, CultureInfo.InvariantCulture);
            if (targetType == typeof(double)) return Convert.ToDouble(val, CultureInfo.InvariantCulture);
            if (targetType == typeof(string)) return val.ToString();

            return val;
        }

        // ── Serialization ─────────────────────────────────────────────────────────

        private static void SerializeValue(object? value, StringBuilder sb, int indentLevel)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            if (value is string s)
            {
                sb.Append('"').Append(EscapeString(s)).Append('"');
                return;
            }

            if (value is bool b)
            {
                sb.Append(b ? "true" : "false");
                return;
            }

            if (value is DateTime dt)
            {
                sb.Append('"').Append(dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)).Append('"');
                return;
            }

            if (value is int || value is long || value is short || value is byte)
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }

            if (value is float f)
            {
                sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            if (value is double d)
            {
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            if (value is IDictionary dict)
            {
                SerializeDictionary(dict, sb, indentLevel);
                return;
            }

            if (value is IEnumerable list)
            {
                SerializeList(list, sb, indentLevel);
                return;
            }

            SerializeObjectFields(value, sb, indentLevel);
        }

        private static void SerializeDictionary(IDictionary dict, StringBuilder sb, int indentLevel)
        {
            bool pretty = indentLevel >= 0;
            if (dict.Count == 0)
            {
                sb.Append("{}");
                return;
            }

            sb.Append('{');
            if (pretty) sb.Append('\n');

            int index = 0;
            foreach (DictionaryEntry entry in dict)
            {
                if (pretty) sb.Append(' ', (indentLevel + 1) * 2);
                sb.Append('"').Append(EscapeString(entry.Key?.ToString() ?? string.Empty)).Append("\":");
                if (pretty) sb.Append(' ');
                SerializeValue(entry.Value, sb, pretty ? indentLevel + 1 : -1);

                if (++index < dict.Count) sb.Append(',');
                if (pretty) sb.Append('\n');
            }

            if (pretty) sb.Append(' ', indentLevel * 2);
            sb.Append('}');
        }

        private static void SerializeList(IEnumerable list, StringBuilder sb, int indentLevel)
        {
            bool pretty = indentLevel >= 0;
            sb.Append('[');
            bool first = true;
            foreach (var item in list)
            {
                if (!first) sb.Append(pretty ? ", " : ",");
                SerializeValue(item, sb, pretty ? indentLevel + 1 : -1);
                first = false;
            }
            sb.Append(']');
        }

        private static void SerializeObjectFields(object obj, StringBuilder sb, int indentLevel)
        {
            bool pretty = indentLevel >= 0;
            Type type = obj.GetType();
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

            var entries = new List<(string Name, object? Value)>();
            foreach (var prop in properties)
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                entries.Add((prop.Name, prop.GetValue(obj, null)));
            }
            foreach (var field in fields)
            {
                entries.Add((field.Name, field.GetValue(obj)));
            }

            if (entries.Count == 0)
            {
                sb.Append("{}");
                return;
            }

            sb.Append('{');
            if (pretty) sb.Append('\n');

            for (int i = 0; i < entries.Count; i++)
            {
                if (pretty) sb.Append(' ', (indentLevel + 1) * 2);
                sb.Append('"').Append(EscapeString(entries[i].Name)).Append("\":");
                if (pretty) sb.Append(' ');
                SerializeValue(entries[i].Value, sb, pretty ? indentLevel + 1 : -1);

                if (i < entries.Count - 1) sb.Append(',');
                if (pretty) sb.Append('\n');
            }

            if (pretty) sb.Append(' ', indentLevel * 2);
            sb.Append('}');
        }

        private static string EscapeString(string str)
        {
            var sb = new StringBuilder(str.Length + 4);
            foreach (char c in str)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        // ── Deserialization / Parser ──────────────────────────────────────────────

        public static object? Deserialize(string json)
        {
            int index = 0;
            return ParseValue(json, ref index);
        }

        private static object? ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) return null;

            char c = json[index];
            if (c == '{') return ParseObject(json, ref index);
            if (c == '[') return ParseArray(json, ref index);
            if (c == '"') return ParseString(json, ref index);
            if (c == 't' || c == 'f') return ParseBool(json, ref index);
            if (c == 'n') return ParseNull(json, ref index);
            if (char.IsDigit(c) || c == '-') return ParseNumber(json, ref index);

            return null;
        }

        private static Dictionary<string, object?> ParseObject(string json, ref int index)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            index++; // skip '{'

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length) break;
                if (json[index] == '}') { index++; break; }

                string key = ParseString(json, ref index);
                SkipWhitespace(json, ref index);

                if (index < json.Length && json[index] == ':') index++;

                object? value = ParseValue(json, ref index);
                dict[key] = value;

                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',') index++;
            }

            return dict;
        }

        private static List<object?> ParseArray(string json, ref int index)
        {
            var list = new List<object?>();
            index++; // skip '['

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length) break;
                if (json[index] == ']') { index++; break; }

                list.Add(ParseValue(json, ref index));

                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',') index++;
            }

            return list;
        }

        private static string ParseString(string json, ref int index)
        {
            var sb = new StringBuilder();
            index++; // skip opening quote

            while (index < json.Length)
            {
                char c = json[index++];
                if (c == '"') break;
                if (c == '\\' && index < json.Length)
                {
                    char escaped = json[index++];
                    switch (escaped)
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
                            if (index + 4 <= json.Length)
                            {
                                string hex = json.Substring(index, 4);
                                if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int unicode))
                                {
                                    sb.Append((char)unicode);
                                    index += 4;
                                }
                            }
                            break;
                        default: sb.Append(escaped); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private static bool ParseBool(string json, ref int index)
        {
            if (json.Substring(index).StartsWith("true", StringComparison.OrdinalIgnoreCase))
            {
                index += 4;
                return true;
            }
            if (json.Substring(index).StartsWith("false", StringComparison.OrdinalIgnoreCase))
            {
                index += 5;
                return false;
            }
            index++;
            return false;
        }

        private static object? ParseNull(string json, ref int index)
        {
            if (json.Substring(index).StartsWith("null", StringComparison.OrdinalIgnoreCase))
            {
                index += 4;
            }
            return null;
        }

        private static object ParseNumber(string json, ref int index)
        {
            int start = index;
            while (index < json.Length && (char.IsDigit(json[index]) || json[index] == '-' || json[index] == '+' || json[index] == '.' || json[index] == 'e' || json[index] == 'E'))
            {
                index++;
            }

            string numStr = json.Substring(start, index - start);
            if (numStr.Contains(".") || numStr.Contains("e") || numStr.Contains("E"))
            {
                if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    return d;
            }
            else
            {
                if (long.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                {
                    if (l <= int.MaxValue && l >= int.MinValue) return (int)l;
                    return l;
                }
            }

            return numStr;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
            {
                index++;
            }
        }
    }
}
