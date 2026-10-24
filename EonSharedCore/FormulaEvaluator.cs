using System;
using System.Globalization;

namespace EonSharedCore
{
    /// <summary>
    /// Safe arithmetic expression evaluator. Direct port of EonTest's FormulaParser.
    /// Allowed: numbers, 'x', + - * / % , parentheses, abs/round/floor/ceil/min/max.
    /// No file/network/script access.
    /// </summary>
    public static class FormulaEvaluator
    {
        public static bool TryEvaluate(string expression, double x, out double result, out string error)
        {
            result = 0.0;
            error = "";
            if (string.IsNullOrWhiteSpace(expression)) { error = "empty formula"; return false; }
            var parser = new Parser(expression, x);
            bool ok = parser.Parse(out result);
            error = parser.Error;
            return ok;
        }

        private sealed class Parser
        {
            private readonly string _text;
            private readonly double _x;
            private int _position;
            private double _result;
            private double _rhs;
            private string _error = "";

            public string Error { get { return _error; } }

            public Parser(string text, double x)
            {
                _text = text;
                _x = x;
            }

            public bool Parse(out double result)
            {
                _position = 0;
                if (!Expression()) { result = 0.0; return false; }
                SkipSpaces();
                if (_position != _text.Length) return Fail("unexpected token", out result);
                if (double.IsNaN(_result) || double.IsInfinity(_result))
                    return Fail("formula result is not finite", out result);
                result = _result;
                return true;
            }

            private bool Expression()
            {
                if (!Term()) return false;
                double value = _rhs;
                while (true)
                {
                    SkipSpaces();
                    if (Match('+')) { if (!Term()) return false; value += _rhs; }
                    else if (Match('-')) { if (!Term()) return false; value -= _rhs; }
                    else { _result = value; return true; }
                }
            }

            private bool Term()
            {
                if (!Factor()) return false;
                double value = _rhs;
                while (true)
                {
                    SkipSpaces();
                    if (Match('*')) { if (!Factor()) return false; value *= _rhs; }
                    else if (Match('/'))
                    {
                        if (!Factor()) return false;
                        if (_rhs == 0.0) return Fail("division by zero");
                        value /= _rhs;
                    }
                    else if (Match('%'))
                    {
                        if (!Factor()) return false;
                        if (_rhs == 0.0) return Fail("modulo by zero");
                        value %= _rhs;
                    }
                    else { _rhs = value; return true; }
                }
            }

            private bool Factor()
            {
                SkipSpaces();
                if (Match('+')) return Factor();
                if (Match('-')) { if (!Factor()) return false; _rhs = -_rhs; return true; }
                if (Match('('))
                {
                    if (!Expression()) return false;
                    double value = _result;
                    SkipSpaces();
                    if (!Match(')')) return Fail("missing ')'");
                    _rhs = value;
                    return true;
                }

                // number (with optional exponent)
                int start = _position;
                bool exponent = false;
                while (_position < _text.Length)
                {
                    char c = _text[_position];
                    if (char.IsDigit(c) || c == '.') { _position++; continue; }
                    if ((c == 'e' || c == 'E') && !exponent)
                    {
                        exponent = true;
                        _position++;
                        if (_position < _text.Length && (_text[_position] == '+' || _text[_position] == '-'))
                            _position++;
                        continue;
                    }
                    break;
                }
                if (_position > start)
                {
                    double number;
                    if (!double.TryParse(_text.Substring(start, _position - start),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                        return Fail("invalid number");
                    _rhs = number;
                    return true;
                }

                // variable x
                if (_position < _text.Length &&
                    string.Compare(_text.Substring(_position, 1), "x", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    _position++; _rhs = _x; return true;
                }

                // functions
                string[] functions = { "abs", "round", "floor", "ceil", "min", "max" };
                foreach (string fn in functions)
                {
                    if (_position + fn.Length <= _text.Length &&
                        string.Compare(_text, _position, fn, 0, fn.Length, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        _position += fn.Length;
                        SkipSpaces();
                        if (!Match('(')) return Fail("missing '(' after function");
                        if (!Expression()) return false;
                        double first = _result;
                        SkipSpaces();
                        if (fn == "abs" || fn == "round" || fn == "floor" || fn == "ceil")
                        {
                            if (!Match(')')) return Fail("function expects one argument");
                            if (fn == "abs") _rhs = Math.Abs(first);
                            else if (fn == "round") _rhs = Math.Round(first);
                            else if (fn == "floor") _rhs = Math.Floor(first);
                            else _rhs = Math.Ceiling(first);
                            return true;
                        }
                        if (!Match(',')) return Fail("function expects two arguments");
                        if (!Expression()) return false;
                        double second = _result;
                        SkipSpaces();
                        if (!Match(')')) return Fail("missing ')'");
                        _rhs = fn == "min" ? Math.Min(first, second) : Math.Max(first, second);
                        return true;
                    }
                }
                return Fail("expected number, x, or supported function");
            }

            private bool Match(char character)
            {
                SkipSpaces();
                if (_position < _text.Length && _text[_position] == character)
                {
                    _position++;
                    return true;
                }
                return false;
            }

            private void SkipSpaces()
            {
                while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++;
            }

            private bool Fail(string message)
            {
                if (string.IsNullOrEmpty(_error)) _error = message;
                return false;
            }

            private bool Fail(string message, out double result)
            {
                result = 0.0;
                return Fail(message);
            }
        }
    }
}
