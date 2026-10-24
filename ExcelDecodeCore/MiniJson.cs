using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ExcelDecodeCore
{
    /// <summary>
    /// Minimal dependency-free JSON parser for net48.
    /// Produces: object -&gt; Dictionary&lt;string,object&gt;, array -&gt; List&lt;object&gt;,
    /// string -&gt; string, number -&gt; long or double, bool -&gt; bool, null -&gt; null.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var parser = new Parser(json);
            object value = parser.ParseValue();
            parser.SkipWhitespace();
            if (parser.Position < json.Length)
                throw new FormatException("Unexpected trailing characters in JSON");
            return value;
        }

        public static bool TryParse(string json, out object value)
        {
            value = null;
            try { value = Parse(json); return true; }
            catch (Exception) { return false; }
        }

        private sealed class Parser
        {
            private readonly string _text;
            public int Position { get; private set; }

            public Parser(string text) { _text = text; }

            public void SkipWhitespace()
            {
                while (Position < _text.Length && char.IsWhiteSpace(_text[Position])) Position++;
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (Position >= _text.Length) throw new FormatException("Unexpected end of JSON");
                char c = _text[Position];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw new FormatException("Unexpected character in JSON at " + Position);
                }
            }

            private void Expect(string word)
            {
                if (Position + word.Length > _text.Length ||
                    string.CompareOrdinal(_text, Position, word, 0, word.Length) != 0)
                    throw new FormatException("Invalid JSON literal at " + Position);
                Position += word.Length;
            }

            private Dictionary<string, object> ParseObject()
            {
                Position++; // consume '{'
                var result = new Dictionary<string, object>();
                SkipWhitespace();
                if (Position < _text.Length && _text[Position] == '}') { Position++; return result; }
                while (true)
                {
                    SkipWhitespace();
                    string key = ParseString();
                    SkipWhitespace();
                    if (Position >= _text.Length || _text[Position] != ':')
                        throw new FormatException("Expected ':' in JSON object");
                    Position++;
                    object value = ParseValue();
                    result[key] = value;
                    SkipWhitespace();
                    if (Position >= _text.Length) throw new FormatException("Unterminated JSON object");
                    char c = _text[Position];
                    if (c == ',') { Position++; continue; }
                    if (c == '}') { Position++; return result; }
                    throw new FormatException("Expected ',' or '}' in JSON object");
                }
            }

            private List<object> ParseArray()
            {
                Position++; // consume '['
                var result = new List<object>();
                SkipWhitespace();
                if (Position < _text.Length && _text[Position] == ']') { Position++; return result; }
                while (true)
                {
                    object value = ParseValue();
                    result.Add(value);
                    SkipWhitespace();
                    if (Position >= _text.Length) throw new FormatException("Unterminated JSON array");
                    char c = _text[Position];
                    if (c == ',') { Position++; continue; }
                    if (c == ']') { Position++; return result; }
                    throw new FormatException("Expected ',' or ']' in JSON array");
                }
            }

            private string ParseString()
            {
                if (Position >= _text.Length || _text[Position] != '"')
                    throw new FormatException("Expected string in JSON");
                Position++; // consume '"'
                var sb = new StringBuilder();
                while (Position < _text.Length)
                {
                    char c = _text[Position];
                    if (c == '"') { Position++; return sb.ToString(); }
                    if (c == '\\')
                    {
                        Position++;
                        if (Position >= _text.Length) throw new FormatException("Unterminated escape in JSON string");
                        char e = _text[Position];
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
                                if (Position + 4 >= _text.Length) throw new FormatException("Invalid unicode escape");
                                string hex = _text.Substring(Position + 1, 4);
                                int code;
                                if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                    throw new FormatException("Invalid unicode escape");
                                sb.Append((char)code);
                                Position += 4;
                                break;
                            default: throw new FormatException("Invalid escape in JSON string");
                        }
                        Position++;
                    }
                    else { sb.Append(c); Position++; }
                }
                throw new FormatException("Unterminated JSON string");
            }

            private object ParseNumber()
            {
                int start = Position;
                bool isDouble = false;
                while (Position < _text.Length)
                {
                    char c = _text[Position];
                    if (c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                    {
                        if (c == '.' || c == 'e' || c == 'E') isDouble = true;
                        Position++;
                    }
                    else if (c >= '0' && c <= '9') Position++;
                    else break;
                }
                string token = _text.Substring(start, Position - start);
                if (token.Length == 0) throw new FormatException("Invalid JSON number");
                if (isDouble)
                {
                    double d;
                    if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                        throw new FormatException("Invalid JSON number: " + token);
                    return d;
                }
                long l;
                if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out l))
                    return l;
                double d2;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out d2))
                    return d2;
                throw new FormatException("Invalid JSON number: " + token);
            }
        }
    }
}
