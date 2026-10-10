using System.Globalization;

namespace SwbfHudEditor.Core.Hud;

/// <summary>
/// A sum typed where a number goes, worked out: <c>+ - * /</c> and brackets, with the usual order, as
/// <c>5.12*2</c>, <c>128/4</c> or <c>(1920 - 64) / 2</c>. Numbers as the files write them.
/// </summary>
public static class NumberExpression
{
    public static bool TryEvaluate(string text, out double value)
    {
        value = 0;
        var parser = new Parser(text);
        if (!parser.Expression(out var result)) return false;
        parser.SkipSpaces();
        if (!parser.AtEnd || !double.IsFinite(result)) return false;
        value = result;
        return true;
    }

    private sealed class Parser(string text)
    {
        private int _at;
        private int _depth;

        public bool AtEnd => _at >= text.Length;

        public void SkipSpaces()
        {
            while (_at < text.Length && char.IsWhiteSpace(text[_at])) _at++;
        }

        private bool Take(char c)
        {
            SkipSpaces();
            if (_at < text.Length && text[_at] == c)
            {
                _at++;
                return true;
            }
            return false;
        }

        public bool Expression(out double value)
        {
            if (!Term(out value)) return false;
            while (true)
            {
                if (Take('+'))
                {
                    if (!Term(out var right)) return false;
                    value += right;
                }
                else if (Take('-'))
                {
                    if (!Term(out var right)) return false;
                    value -= right;
                }
                else return true;
            }
        }

        private bool Term(out double value)
        {
            if (!Unary(out value)) return false;
            while (true)
            {
                if (Take('*'))
                {
                    if (!Unary(out var right)) return false;
                    value *= right;
                }
                else if (Take('/'))
                {
                    if (!Unary(out var right)) return false;
                    value /= right;
                }
                else return true;
            }
        }

        private bool Unary(out double value)
        {
            if (Take('-'))
            {
                if (!Unary(out value)) return false;
                value = -value;
                return true;
            }
            if (Take('+')) return Unary(out value);
            return Primary(out value);
        }

        private bool Primary(out double value)
        {
            value = 0;
            if (Take('('))
            {
                if (++_depth > 64 || !Expression(out value)) return false;
                _depth--;
                return Take(')');
            }
            SkipSpaces();
            var start = _at;
            while (_at < text.Length && (char.IsDigit(text[_at]) || text[_at] == '.')) _at++;
            if (_at < text.Length && (text[_at] == 'e' || text[_at] == 'E'))
            {
                var mark = _at++;
                if (_at < text.Length && (text[_at] == '+' || text[_at] == '-')) _at++;
                var digits = _at;
                while (_at < text.Length && char.IsDigit(text[_at])) _at++;
                if (_at == digits) _at = mark;   // not an exponent after all
            }
            return _at > start && double.TryParse(text.AsSpan(start, _at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
