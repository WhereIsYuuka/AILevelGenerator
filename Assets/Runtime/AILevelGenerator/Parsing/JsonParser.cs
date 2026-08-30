using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AILevelGenerator.Runtime.Parsing
{
    /// <summary>
    /// JSON 解析失败异常（含位置诊断）。容错解析的"最终失败"边界：
    /// 提取不到 JSON 内容 / 结构严重损坏不可修复时抛出，由调用方转为 IsValid=false + 中文错误。
    /// </summary>
    public class JsonParseException : Exception
    {
        public int Position { get; }

        public JsonParseException(string message, int position = -1)
            : base(position >= 0 ? $"{message}（位置 {position}）" : message)
        {
            Position = position;
        }
    }

    /// <summary>
    /// 通用 JSON 值树节点（零依赖，供 LLM 响应容错解析使用）。
    /// 附带容错类型转换辅助：字符串数字（"42"/"1.5"/"true"）也可转数字/布尔，失败返回兜底值。
    /// </summary>
    public class JsonValue
    {
        public enum Kind { Null, Boolean, Number, String, Array, Object }

        public Kind ValueKind;
        public bool BoolValue;
        public double NumberValue;
        public string StringValue;
        public List<JsonValue> ArrayValue = new();
        public Dictionary<string, JsonValue> ObjectValue = new();

        public bool IsObject => ValueKind == Kind.Object;
        public bool IsArray => ValueKind == Kind.Array;

        // —— 结构访问 ——

        /// <summary> 对象字段访问，缺字段返回 null </summary>
        public JsonValue Get(string key)
        {
            if (ValueKind != Kind.Object) return null;
            return ObjectValue.TryGetValue(key, out var v) ? v : null;
        }

        /// <summary> 数组元素访问，越界返回 null </summary>
        public JsonValue GetAt(int index)
        {
            if (ValueKind != Kind.Array || index < 0 || index >= ArrayValue.Count) return null;
            return ArrayValue[index];
        }

        public bool ContainsKey(string key) => ValueKind == Kind.Object && ObjectValue.ContainsKey(key);

        // —— 容错类型转换（自身 → 目标类型，失败返回 fallback） ——

        /// <summary> 转字符串：数字按不变文化格式化，布尔转 "true"/"false"，其他类型返回 fallback </summary>
        public string AsString(string fallback)
        {
            switch (ValueKind)
            {
                case Kind.String: return StringValue;
                case Kind.Number: return NumberValue.ToString(CultureInfo.InvariantCulture);
                case Kind.Boolean: return BoolValue ? "true" : "false";
                default: return fallback;
            }
        }

        /// <summary> 转 int：数字截断取整；字符串 "42" 可解析；失败返回 fallback </summary>
        public int AsInt(int fallback)
        {
            switch (ValueKind)
            {
                case Kind.Number: return (int)NumberValue;
                case Kind.String:
                    return int.TryParse(StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;
                default: return fallback;
            }
        }

        /// <summary> 转 float：数字直接转；字符串 "1.5" 可解析；失败返回 fallback </summary>
        public float AsFloat(float fallback)
        {
            switch (ValueKind)
            {
                case Kind.Number: return (float)NumberValue;
                case Kind.String:
                    return float.TryParse(StringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : fallback;
                default: return fallback;
            }
        }

        /// <summary> 转 bool：布尔直接转；字符串 "true"/"false"（不区分大小写）可解析；失败返回 fallback </summary>
        public bool AsBool(bool fallback)
        {
            switch (ValueKind)
            {
                case Kind.Boolean: return BoolValue;
                case Kind.String:
                    if (StringValue == null) return fallback;
                    if (StringValue.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
                    if (StringValue.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
                    return fallback;
                default: return fallback;
            }
        }

        // —— 对象字段便捷访问（缺字段/类型不符 → fallback） ——

        public string GetString(string key, string fallback) => Get(key)?.AsString(fallback) ?? fallback;
        public int GetInt(string key, int fallback) => Get(key)?.AsInt(fallback) ?? fallback;
        public float GetFloat(string key, float fallback) => Get(key)?.AsFloat(fallback) ?? fallback;
        public bool GetBool(string key, bool fallback) => Get(key)?.AsBool(fallback) ?? fallback;

        // —— 静态工厂 ——

        public static JsonValue CreateNull() => new() { ValueKind = Kind.Null };
        public static JsonValue FromBool(bool value) => new() { ValueKind = Kind.Boolean, BoolValue = value };
        public static JsonValue FromNumber(double value) => new() { ValueKind = Kind.Number, NumberValue = value };
        public static JsonValue FromString(string value) => new() { ValueKind = Kind.String, StringValue = value ?? string.Empty };
        public static JsonValue CreateArray() => new() { ValueKind = Kind.Array };
        public static JsonValue CreateObject() => new() { ValueKind = Kind.Object };
    }

    /// <summary>
    /// 零依赖 JSON 解析器（容错语义）：
    ///   1. 提取：从文本中定位第一个 { 或 [，扫描配对结束位置（剥离 ```json 围栏与前后解释文字）
    ///   2. 修复：容忍尾逗号、字符串内非法控制字符、BOM、额外空白
    ///   3. 失败边界：无 JSON 内容 / 括号不配对（截断）/ 语法不可修复 → JsonParseException
    /// </summary>
    public static class JsonParser
    {
        /// <summary> 解析文本中的 JSON（自动提取 + 容错），失败抛 JsonParseException </summary>
        public static JsonValue Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new JsonParseException("JSON 内容为空");

            var start = FindStartIndex(raw);
            var end = FindEndIndex(raw, start);
            var text = raw.Substring(start, end - start + 1);

            var parser = new InternalParser(text);
            var value = parser.ParseDocument();
            return value;
        }

        /// <summary> 序列化转义（请求体等 JSON 字符串值的转义，\ " \n \r \t \b \f \uXXXX） </summary>
        public static string EscapeString(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var sb = new StringBuilder(raw.Length + 8);
            foreach (var c in raw)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary> 定位第一个 { 或 [（取靠前者），找不到说明无 JSON 内容 </summary>
        private static int FindStartIndex(string raw)
        {
            var brace = raw.IndexOf('{');
            var bracket = raw.IndexOf('[');
            if (brace < 0 && bracket < 0)
                throw new JsonParseException("未找到 JSON 内容（缺少 { 或 [）");
            if (brace < 0) return bracket;
            if (bracket < 0) return brace;
            return Math.Min(brace, bracket);
        }

        /// <summary> 从 start 扫描配对括号结束位置（跳过字符串内括号与转义），不配对视为截断 </summary>
        private static int FindEndIndex(string raw, int start)
        {
            var depth = 0;
            var inString = false;
            for (var i = start; i < raw.Length; i++)
            {
                var c = raw[i];
                if (inString)
                {
                    if (c == '\\') { i++; continue; } // 跳过转义字符
                    if (c == '"') inString = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inString = true; break;
                    case '{': case '[': depth++; break;
                    case '}': case ']':
                        depth--;
                        if (depth == 0) return i;
                        break;
                }
            }
            throw new JsonParseException("JSON 不完整（括号未配对，内容可能被截断）");
        }

        /// <summary> 内部递归下降解析器（容忍尾逗号、BOM、字符串内控制字符） </summary>
        private class InternalParser
        {
            private readonly string _text;
            private int _pos;

            public InternalParser(string text)
            {
                _text = text;
            }

            public JsonValue ParseDocument()
            {
                var value = ParseValue();
                return value;
            }

            private JsonValue ParseValue()
            {
                SkipWhitespace();
                if (_pos >= _text.Length)
                    throw new JsonParseException("JSON 意外结束", _pos);

                var c = _text[_pos];
                switch (c)
                {
                    case '"': return ParseString();
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case 't': Expect("true"); return JsonValue.FromBool(true);
                    case 'f': Expect("false"); return JsonValue.FromBool(false);
                    case 'n': Expect("null"); return JsonValue.CreateNull();
                    case '-': return ParseNumber();
                    default:
                        if (c >= '0' && c <= '9') return ParseNumber();
                        throw new JsonParseException($"无法识别的 JSON 内容：'{c}'", _pos);
                }
            }

            private JsonValue ParseString()
            {
                _pos++; // 跳过开头引号
                var sb = new StringBuilder();
                while (_pos < _text.Length)
                {
                    var c = _text[_pos];
                    if (c == '"')
                    {
                        _pos++;
                        return JsonValue.FromString(sb.ToString());
                    }
                    if (c == '\\')
                    {
                        _pos++;
                        if (_pos >= _text.Length) break;
                        var e = _text[_pos];
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
                                if (_pos + 4 < _text.Length)
                                {
                                    var hex = _text.Substring(_pos + 1, 4);
                                    if (ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                                    {
                                        sb.Append((char)code);
                                        _pos += 4;
                                    }
                                }
                                break;
                            default: sb.Append(e); break; // 未知转义：容错保留原字符
                        }
                    }
                    else if (c < 0x20)
                    {
                        // 字符串内非法控制字符：容错跳过（修复策略）
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    _pos++;
                }
                throw new JsonParseException("字符串未闭合（缺少结尾引号）", _pos);
            }

            private JsonValue ParseNumber()
            {
                var start = _pos;
                if (_pos < _text.Length && _text[_pos] == '-') _pos++;
                while (_pos < _text.Length && char.IsDigit(_text[_pos])) _pos++;
                if (_pos < _text.Length && _text[_pos] == '.')
                {
                    _pos++;
                    while (_pos < _text.Length && char.IsDigit(_text[_pos])) _pos++;
                }
                if (_pos < _text.Length && (_text[_pos] == 'e' || _text[_pos] == 'E'))
                {
                    _pos++;
                    if (_pos < _text.Length && (_text[_pos] == '+' || _text[_pos] == '-')) _pos++;
                    while (_pos < _text.Length && char.IsDigit(_text[_pos])) _pos++;
                }
                var token = _text.Substring(start, _pos - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    throw new JsonParseException($"数字格式非法：{token}", start);
                return JsonValue.FromNumber(value);
            }

            private JsonValue ParseObject()
            {
                _pos++; // 跳过 {
                var obj = JsonValue.CreateObject();
                SkipWhitespace();
                if (ConsumeIf('}')) return obj; // 空对象 {}

                while (true)
                {
                    SkipWhitespace();
                    if (_pos >= _text.Length)
                        throw new JsonParseException("对象未闭合（缺少 }）", _pos);
                    if (_text[_pos] != '"')
                        throw new JsonParseException("对象键必须是字符串", _pos);

                    var key = ParseString().StringValue;
                    SkipWhitespace();
                    if (!ConsumeIf(':'))
                        throw new JsonParseException($"对象键 \"{key}\" 缺少冒号", _pos);

                    var value = ParseValue();
                    obj.ObjectValue[key] = value; // 重复键：后者覆盖

                    SkipWhitespace();
                    if (ConsumeIf('}')) return obj;
                    if (!ConsumeIf(','))
                        throw new JsonParseException("对象元素间缺少逗号", _pos);
                    SkipWhitespace();
                    if (ConsumeIf('}')) return obj; // 容忍尾逗号
                }
            }

            private JsonValue ParseArray()
            {
                _pos++; // 跳过 [
                var arr = JsonValue.CreateArray();
                SkipWhitespace();
                if (ConsumeIf(']')) return arr; // 空数组 []

                while (true)
                {
                    SkipWhitespace();
                    arr.ArrayValue.Add(ParseValue());
                    SkipWhitespace();
                    if (ConsumeIf(']')) return arr;
                    if (!ConsumeIf(','))
                        throw new JsonParseException("数组元素间缺少逗号", _pos);
                    SkipWhitespace();
                    if (ConsumeIf(']')) return arr; // 容忍尾逗号
                }
            }

            /// <summary> 期待固定关键字（true/false/null），不匹配则失败 </summary>
            private void Expect(string word)
            {
                if (_pos + word.Length > _text.Length || _text.Substring(_pos, word.Length) != word)
                    throw new JsonParseException($"语法错误（期望 {word}）", _pos);
                _pos += word.Length;
            }

            /// <summary> 跳过空白与 BOM </summary>
            private void SkipWhitespace()
            {
                while (_pos < _text.Length)
                {
                    var c = _text[_pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '﻿') _pos++;
                    else break;
                }
            }

            /// <summary> 当前字符匹配则消费并返回 true </summary>
            private bool ConsumeIf(char expected)
            {
                if (_pos < _text.Length && _text[_pos] == expected)
                {
                    _pos++;
                    return true;
                }
                return false;
            }
        }
    }
}
