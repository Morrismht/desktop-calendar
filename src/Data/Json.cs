using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DesktopCalendar.Data
{
    /// <summary>
    /// 手写最小 JSON 读写（零依赖，不用 System.Text.Json / Newtonsoft）。
    /// 支持：Dictionary&lt;string,object&gt; / List&lt;object&gt; / 任意 IDictionary / 任意 IEnumerable /
    /// string / bool / 整数 / double / DateTime / DateTime? / null。
    /// 日期统一写成 ISO "yyyy-MM-ddTHH:mm:ss"，null 写成 null。
    /// 中文等非 ASCII 字符原样输出（UTF-8 文件），不做 \u 转义。
    /// </summary>
    public static class Json
    {
        public const string DateFormat = "yyyy-MM-ddTHH:mm:ss";

        // ---------------- 写 ----------------

        /// <summary>序列化。支持 Dictionary&lt;string,object&gt;/List&lt;object&gt;/string/bool/整数/double/DateTime/DateTime?/null。</summary>
        public static string Write(object value)
        {
            StringBuilder sb = new StringBuilder(256);
            WriteValue(sb, value);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object v)
        {
            if (v == null)
            {
                sb.Append("null");
                return;
            }

            string s = v as string;
            if (s != null)
            {
                sb.Append(Esc(s));
                return;
            }

            if (v is bool)
            {
                sb.Append(((bool)v) ? "true" : "false");
                return;
            }

            if (v is DateTime)
            {
                sb.Append(Esc(((DateTime)v).ToString(DateFormat, CultureInfo.InvariantCulture)));
                return;
            }

            if (v is int || v is long || v is short || v is byte || v is sbyte || v is uint || v is ulong || v is ushort)
            {
                sb.Append(Convert.ToInt64(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (v is double || v is float || v is decimal)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d))
                {
                    sb.Append("null");
                    return;
                }
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            if (v is Enum)
            {
                sb.Append(Convert.ToInt64(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                return;
            }

            IDictionary<string, object> map = v as IDictionary<string, object>;
            if (map != null)
            {
                WriteObject(sb, map);
                return;
            }

            IDictionary raw = v as IDictionary;
            if (raw != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry e in raw)
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }
                    first = false;
                    sb.Append(Esc(Convert.ToString(e.Key, CultureInfo.InvariantCulture)));
                    sb.Append(':');
                    WriteValue(sb, e.Value);
                }
                sb.Append('}');
                return;
            }

            IEnumerable seq = v as IEnumerable;
            if (seq != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object o in seq)
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }
                    first = false;
                    WriteValue(sb, o);
                }
                sb.Append(']');
                return;
            }

            // 兜底：转字符串
            sb.Append(Esc(Convert.ToString(v, CultureInfo.InvariantCulture)));
        }

        private static void WriteObject(StringBuilder sb, IDictionary<string, object> map)
        {
            sb.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> kv in map)
            {
                if (!first)
                {
                    sb.Append(',');
                }
                first = false;
                sb.Append(Esc(kv.Key));
                sb.Append(':');
                WriteValue(sb, kv.Value);
            }
            sb.Append('}');
        }

        /// <summary>转成带引号的 JSON 字符串字面量（含引号）。null 返回 "null"。</summary>
        public static string Esc(string s)
        {
            if (s == null)
            {
                return "null";
            }
            StringBuilder sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        // ---------------- 读 ----------------

        /// <summary>解析单个 JSON 对象。</summary>
        public static Dictionary<string, object> ReadObject(string text)
        {
            if (text == null)
            {
                throw new FormatException("JSON 文本为空");
            }
            Parser p = new Parser(text);
            object v = p.ParseRoot();
            Dictionary<string, object> map = v as Dictionary<string, object>;
            if (map == null)
            {
                throw new FormatException("JSON 根节点不是对象");
            }
            return map;
        }

        /// <summary>解析 "对象数组"，返回每个对象。</summary>
        public static List<Dictionary<string, object>> ReadObjectArray(string text)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            if (text == null || text.Trim().Length == 0)
            {
                return result;
            }
            Parser p = new Parser(text);
            object v = p.ParseRoot();
            if (v == null)
            {
                return result;
            }
            List<object> arr = v as List<object>;
            if (arr == null)
            {
                throw new FormatException("JSON 根节点不是数组");
            }
            for (int i = 0; i < arr.Count; i++)
            {
                Dictionary<string, object> map = arr[i] as Dictionary<string, object>;
                if (map == null)
                {
                    throw new FormatException(string.Format("数组第 {0} 项不是对象", i));
                }
                result.Add(map);
            }
            return result;
        }

        // ---------------- 取值助手 ----------------

        /// <summary>把解析出来的数组值（List&lt;object&gt;）转成对象列表；非数组返回空列表。</summary>
        public static List<Dictionary<string, object>> AsObjectArray(object v)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            List<object> arr = v as List<object>;
            if (arr == null)
            {
                return result;
            }
            for (int i = 0; i < arr.Count; i++)
            {
                Dictionary<string, object> map = arr[i] as Dictionary<string, object>;
                if (map != null)
                {
                    result.Add(map);
                }
            }
            return result;
        }

        public static string Str(Dictionary<string, object> o, string k, string def = null)
        {
            object v;
            if (o == null || !o.TryGetValue(k, out v) || v == null)
            {
                return def;
            }
            string s = v as string;
            if (s != null)
            {
                return s;
            }
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static int Int(Dictionary<string, object> o, string k, int def = 0)
        {
            object v;
            if (o == null || !o.TryGetValue(k, out v) || v == null)
            {
                return def;
            }
            try
            {
                if (v is int)
                {
                    return (int)v;
                }
                if (v is long)
                {
                    return (int)(long)v;
                }
                if (v is double)
                {
                    return (int)Math.Round((double)v);
                }
                string s = v as string;
                if (s != null)
                {
                    int r;
                    if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out r))
                    {
                        return r;
                    }
                    return def;
                }
                return Convert.ToInt32(v, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return def;
            }
        }

        public static bool Bool(Dictionary<string, object> o, string k, bool def = false)
        {
            object v;
            if (o == null || !o.TryGetValue(k, out v) || v == null)
            {
                return def;
            }
            if (v is bool)
            {
                return (bool)v;
            }
            string s = v as string;
            if (s != null)
            {
                bool b;
                if (bool.TryParse(s, out b))
                {
                    return b;
                }
                return def;
            }
            try
            {
                return Convert.ToInt64(v, CultureInfo.InvariantCulture) != 0L;
            }
            catch (Exception)
            {
                return def;
            }
        }

        public static DateTime? Date(Dictionary<string, object> o, string k)
        {
            object v;
            if (o == null || !o.TryGetValue(k, out v) || v == null)
            {
                return null;
            }
            if (v is DateTime)
            {
                return (DateTime)v;
            }
            string s = v as string;
            if (s == null)
            {
                s = Convert.ToString(v, CultureInfo.InvariantCulture);
            }
            if (s == null || s.Trim().Length == 0)
            {
                return null;
            }
            s = s.Trim();
            DateTime d;
            string[] formats = new string[]
            {
                DateFormat, "yyyy-MM-ddTHH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm", "yyyy-MM-dd"
            };
            if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
            {
                return d;
            }
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
            {
                return d;
            }
            return null;
        }

        // ---------------- 递归下降解析器 ----------------

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s)
            {
                _s = s;
                _i = 0;
            }

            public object ParseRoot()
            {
                SkipWs();
                object v = ParseValue();
                SkipWs();
                return v;
            }

            private void SkipWs()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\uFEFF')
                    {
                        _i++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            private char Peek()
            {
                if (_i >= _s.Length)
                {
                    throw new FormatException("JSON 意外结束");
                }
                return _s[_i];
            }

            private void Expect(char c)
            {
                if (_i >= _s.Length || _s[_i] != c)
                {
                    throw new FormatException(string.Format("JSON 期望 '{0}'，位置 {1}", c, _i));
                }
                _i++;
            }

            private object ParseValue()
            {
                SkipWs();
                char c = Peek();
                if (c == '{')
                {
                    return ParseObject();
                }
                if (c == '[')
                {
                    return ParseArray();
                }
                if (c == '"')
                {
                    return ParseString();
                }
                if (c == 't')
                {
                    Literal("true");
                    return true;
                }
                if (c == 'f')
                {
                    Literal("false");
                    return false;
                }
                if (c == 'n')
                {
                    Literal("null");
                    return null;
                }
                return ParseNumber();
            }

            private void Literal(string word)
            {
                if (_i + word.Length > _s.Length || string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0)
                {
                    throw new FormatException(string.Format("JSON 非法字面量，位置 {0}", _i));
                }
                _i += word.Length;
            }

            private Dictionary<string, object> ParseObject()
            {
                Dictionary<string, object> map = new Dictionary<string, object>();
                Expect('{');
                SkipWs();
                if (Peek() == '}')
                {
                    _i++;
                    return map;
                }
                while (true)
                {
                    SkipWs();
                    string key = ParseString();
                    SkipWs();
                    Expect(':');
                    object val = ParseValue();
                    map[key] = val;
                    SkipWs();
                    char c = Peek();
                    if (c == ',')
                    {
                        _i++;
                        continue;
                    }
                    if (c == '}')
                    {
                        _i++;
                        return map;
                    }
                    throw new FormatException(string.Format("JSON 对象缺少 , 或 }}，位置 {0}", _i));
                }
            }

            private List<object> ParseArray()
            {
                List<object> list = new List<object>();
                Expect('[');
                SkipWs();
                if (Peek() == ']')
                {
                    _i++;
                    return list;
                }
                while (true)
                {
                    object val = ParseValue();
                    list.Add(val);
                    SkipWs();
                    char c = Peek();
                    if (c == ',')
                    {
                        _i++;
                        continue;
                    }
                    if (c == ']')
                    {
                        _i++;
                        return list;
                    }
                    throw new FormatException(string.Format("JSON 数组缺少 , 或 ]，位置 {0}", _i));
                }
            }

            private string ParseString()
            {
                Expect('"');
                StringBuilder sb = new StringBuilder();
                while (true)
                {
                    if (_i >= _s.Length)
                    {
                        throw new FormatException("JSON 字符串未闭合");
                    }
                    char c = _s[_i];
                    _i++;
                    if (c == '"')
                    {
                        return sb.ToString();
                    }
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (_i >= _s.Length)
                    {
                        throw new FormatException("JSON 转义未闭合");
                    }
                    char e = _s[_i];
                    _i++;
                    switch (e)
                    {
                        case '"':
                            sb.Append('"');
                            break;
                        case '\\':
                            sb.Append('\\');
                            break;
                        case '/':
                            sb.Append('/');
                            break;
                        case 'b':
                            sb.Append('\b');
                            break;
                        case 'f':
                            sb.Append('\f');
                            break;
                        case 'n':
                            sb.Append('\n');
                            break;
                        case 'r':
                            sb.Append('\r');
                            break;
                        case 't':
                            sb.Append('\t');
                            break;
                        case 'u':
                            if (_i + 4 > _s.Length)
                            {
                                throw new FormatException("JSON \\u 转义不完整");
                            }
                            string hex = _s.Substring(_i, 4);
                            _i += 4;
                            sb.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            break;
                        default:
                            throw new FormatException(string.Format("JSON 未知转义 \\{0}", e));
                    }
                }
            }

            private object ParseNumber()
            {
                int start = _i;
                if (_i < _s.Length && (_s[_i] == '-' || _s[_i] == '+'))
                {
                    _i++;
                }
                bool isFloat = false;
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c >= '0' && c <= '9')
                    {
                        _i++;
                    }
                    else if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                    {
                        isFloat = isFloat || c == '.' || c == 'e' || c == 'E';
                        _i++;
                    }
                    else
                    {
                        break;
                    }
                }
                string token = _s.Substring(start, _i - start);
                if (token.Length == 0)
                {
                    throw new FormatException(string.Format("JSON 非法数值，位置 {0}", start));
                }
                if (!isFloat)
                {
                    int iv;
                    if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out iv))
                    {
                        return iv;
                    }
                    long lv;
                    if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out lv))
                    {
                        return lv;
                    }
                }
                double dv;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out dv))
                {
                    return dv;
                }
                throw new FormatException(string.Format("JSON 非法数值: {0}", token));
            }
        }
    }
}
