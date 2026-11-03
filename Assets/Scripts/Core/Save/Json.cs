using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TradingEmpire.Core
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>A tiny JSON document model (no dependencies, same behaviour in Unity and in plain .NET). Integers up to 2^53 are exact.</summary>
    public sealed class JsonValue
    {
        public JsonKind Kind;
        public bool Bool;
        public double Number;
        public string String;
        public List<JsonValue> Array;
        /// <summary>Insertion-ordered object members.</summary>
        public List<KeyValuePair<string, JsonValue>> Members;

        public static readonly JsonValue Null = new JsonValue { Kind = JsonKind.Null };
        public static JsonValue Of(bool b) { return new JsonValue { Kind = JsonKind.Bool, Bool = b }; }
        public static JsonValue Of(double n) { return new JsonValue { Kind = JsonKind.Number, Number = n }; }
        public static JsonValue Of(long n) { return new JsonValue { Kind = JsonKind.Number, Number = n }; }
        public static JsonValue Of(string s) { return s == null ? Null : new JsonValue { Kind = JsonKind.String, String = s }; }
        public static JsonValue Arr() { return new JsonValue { Kind = JsonKind.Array, Array = new List<JsonValue>() }; }
        public static JsonValue Obj() { return new JsonValue { Kind = JsonKind.Object, Members = new List<KeyValuePair<string, JsonValue>>() }; }

        public JsonValue Add(JsonValue v) { Array.Add(v); return this; }
        public JsonValue Set(string key, JsonValue v)
        {
            if (v == null) return this; // absent = omitted, like an optional field
            for (int i = 0; i < Members.Count; i++) if (Members[i].Key == key) { Members[i] = new KeyValuePair<string, JsonValue>(key, v); return this; }
            Members.Add(new KeyValuePair<string, JsonValue>(key, v));
            return this;
        }
        public JsonValue Set(string key, long n) { return Set(key, Of(n)); }
        public JsonValue Set(string key, double n) { return Set(key, Of(n)); }
        public JsonValue Set(string key, string s) { return Set(key, Of(s)); }
        public JsonValue Set(string key, bool b) { return Set(key, Of(b)); }

        public bool IsNull { get { return Kind == JsonKind.Null; } }

        /// <summary>Member by key, or null when absent or this is not an object.</summary>
        public JsonValue Get(string key)
        {
            if (Kind != JsonKind.Object) return null;
            foreach (var m in Members) if (m.Key == key) return m.Value;
            return null;
        }

        public long AsLong() { if (Kind != JsonKind.Number || Number != Math.Floor(Number)) throw new JsonException("Expected an integer"); return (long)Number; }
        public double AsDouble() { if (Kind != JsonKind.Number) throw new JsonException("Expected a number"); return Number; }
        public string AsString() { if (Kind != JsonKind.String) throw new JsonException("Expected a string"); return String; }
        public bool AsBool() { if (Kind != JsonKind.Bool) throw new JsonException("Expected true or false"); return Bool; }
        public List<JsonValue> AsArray() { if (Kind != JsonKind.Array) throw new JsonException("Expected a list"); return Array; }
        public List<KeyValuePair<string, JsonValue>> AsObject() { if (Kind != JsonKind.Object) throw new JsonException("Expected an object"); return Members; }

        public override string ToString() { return Json.Serialize(this); }
    }

    public sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }

    public static class Json
    {
        public static JsonValue Parse(string text)
        {
            var p = new Parser(text);
            var v = p.ParseValue();
            p.SkipWs();
            if (!p.AtEnd) throw new JsonException("Unexpected text after the end of the JSON");
            return v;
        }

        public static string Serialize(JsonValue v, bool pretty = false)
        {
            var sb = new StringBuilder();
            Write(sb, v, pretty, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, JsonValue v, bool pretty, int depth)
        {
            switch (v.Kind)
            {
                case JsonKind.Null: sb.Append("null"); break;
                case JsonKind.Bool: sb.Append(v.Bool ? "true" : "false"); break;
                case JsonKind.Number:
                    if (double.IsNaN(v.Number) || double.IsInfinity(v.Number)) throw new JsonException("Cannot save a non-finite number");
                    sb.Append(v.Number == Math.Floor(v.Number) && Math.Abs(v.Number) < 9e15 ? ((long)v.Number).ToString(CultureInfo.InvariantCulture) : v.Number.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case JsonKind.String: WriteString(sb, v.String); break;
                case JsonKind.Array:
                    if (v.Array.Count == 0) { sb.Append("[]"); break; }
                    sb.Append('[');
                    for (int i = 0; i < v.Array.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (pretty) { sb.Append('\n').Append(' ', (depth + 1) * 2); }
                        Write(sb, v.Array[i], pretty, depth + 1);
                    }
                    if (pretty) sb.Append('\n').Append(' ', depth * 2);
                    sb.Append(']');
                    break;
                case JsonKind.Object:
                    if (v.Members.Count == 0) { sb.Append("{}"); break; }
                    sb.Append('{');
                    for (int i = 0; i < v.Members.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (pretty) { sb.Append('\n').Append(' ', (depth + 1) * 2); }
                        WriteString(sb, v.Members[i].Key);
                        sb.Append(pretty ? ": " : ":");
                        Write(sb, v.Members[i].Value, pretty, depth + 1);
                    }
                    if (pretty) sb.Append('\n').Append(' ', depth * 2);
                    sb.Append('}');
                    break;
            }
        }

        static void WriteString(StringBuilder sb, string s)
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
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        sealed class Parser
        {
            readonly string _s;
            int _i;
            int _depth;
            public Parser(string s) { _s = s.Length > 0 && s[0] == '﻿' ? s.Substring(1) : s; }
            public bool AtEnd { get { return _i >= _s.Length; } }

            public void SkipWs() { while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\n' || _s[_i] == '\r' || _s[_i] == '\t')) _i++; }

            char Peek() { if (_i >= _s.Length) throw new JsonException("Unexpected end of the JSON"); return _s[_i]; }

            public JsonValue ParseValue()
            {
                SkipWs();
                char c = Peek();
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return JsonValue.Of(ParseString());
                if (c == 't') { Expect("true"); return JsonValue.Of(true); }
                if (c == 'f') { Expect("false"); return JsonValue.Of(false); }
                if (c == 'n') { Expect("null"); return JsonValue.Null; }
                return ParseNumber();
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw new JsonException("Unexpected text at position " + _i);
                _i += word.Length;
            }

            JsonValue ParseNumber()
            {
                int start = _i;
                if (_i < _s.Length && _s[_i] == '-') _i++;
                while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.' || _s[_i] == 'e' || _s[_i] == 'E' || _s[_i] == '+' || _s[_i] == '-')) _i++;
                if (!double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) throw new JsonException("Bad number at position " + start);
                return JsonValue.Of(d);
            }

            string ParseString()
            {
                _i++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (_i >= _s.Length) throw new JsonException("Unterminated string");
                    char c = _s[_i++];
                    if (c == '"') break;
                    if (c != '\\') { sb.Append(c); continue; }
                    if (_i >= _s.Length) throw new JsonException("Unterminated string");
                    char e = _s[_i++];
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
                            if (_i + 4 > _s.Length) throw new JsonException("Bad \\u escape");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: throw new JsonException("Bad escape in string");
                    }
                }
                return sb.ToString();
            }

            JsonValue ParseArray()
            {
                if (++_depth > 64) throw new JsonException("The JSON is nested too deeply");
                _i++;
                var arr = JsonValue.Arr();
                SkipWs();
                if (Peek() == ']') { _i++; _depth--; return arr; }
                while (true)
                {
                    arr.Add(ParseValue());
                    SkipWs();
                    char c = Peek();
                    _i++;
                    if (c == ']') break;
                    if (c != ',') throw new JsonException("Expected , or ] at position " + (_i - 1));
                }
                _depth--;
                return arr;
            }

            JsonValue ParseObject()
            {
                if (++_depth > 64) throw new JsonException("The JSON is nested too deeply");
                _i++;
                var obj = JsonValue.Obj();
                SkipWs();
                if (Peek() == '}') { _i++; _depth--; return obj; }
                while (true)
                {
                    SkipWs();
                    if (Peek() != '"') throw new JsonException("Expected a key at position " + _i);
                    string key = ParseString();
                    SkipWs();
                    if (Peek() != ':') throw new JsonException("Expected : at position " + _i);
                    _i++;
                    obj.Members.Add(new KeyValuePair<string, JsonValue>(key, ParseValue()));
                    SkipWs();
                    char c = Peek();
                    _i++;
                    if (c == '}') break;
                    if (c != ',') throw new JsonException("Expected , or } at position " + (_i - 1));
                }
                _depth--;
                return obj;
            }
        }
    }
}
