using System.Globalization;
using System.Text;

namespace SwbfHudEditor.Core.Events;

/// <summary>
/// The part of C's <c>snprintf</c> HUD formats use, with one argument: flags <c>-+ 0#</c>, a width, a
/// precision and the conversions <c>d i u x X o c f F e E g G s %</c>. An integer conversion given a
/// float takes its integer part; a float conversion given an integer, its value.
/// </summary>
public static class CFormat
{
    public static string Format(string? format, double value)
    {
        if (format is null) return string.Empty;
        var output = new StringBuilder();
        var used = false;
        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (c != '%')
            {
                output.Append(c);
                continue;
            }
            var start = i++;
            if (i < format.Length && format[i] == '%')
            {
                output.Append('%');
                continue;
            }
            bool left = false, plus = false, space = false, zero = false, alt = false;
            for (; i < format.Length; i++)
            {
                var f = format[i];
                if (f == '-') left = true;
                else if (f == '+') plus = true;
                else if (f == ' ') space = true;
                else if (f == '0') zero = true;
                else if (f == '#') alt = true;
                else break;
            }
            var width = 0;
            for (; i < format.Length && char.IsDigit(format[i]); i++) width = width * 10 + (format[i] - '0');
            int? precision = null;
            if (i < format.Length && format[i] == '.')
            {
                i++;
                var p = 0;
                for (; i < format.Length && char.IsDigit(format[i]); i++) p = p * 10 + (format[i] - '0');
                precision = p;
            }
            // Length modifiers change nothing here.
            while (i < format.Length && format[i] is 'h' or 'l' or 'L' or 'I' or 'q' or 'z' or 'j' or 't') i++;
            if (i >= format.Length)
            {
                output.Append(format, start, format.Length - start);
                break;
            }
            var conversion = format[i];
            // Only the first conversion has an argument; later ones print 0, close to what reading past
            // the one argument gives in practice.
            var arg = used ? 0d : value;
            used = true;
            var text = Convert(conversion, arg, precision, plus, space, alt);
            if (text is null)
            {
                output.Append(format, start, i - start + 1);
                continue;
            }
            // '0' pads numbers with zeros, unless '-' is given or an integer has a precision.
            var integerConversion = conversion is 'd' or 'i' or 'u' or 'x' or 'X' or 'o';
            var number = integerConversion || conversion is 'f' or 'F' or 'e' or 'E' or 'g' or 'G';
            var padZero = zero && !left && number && !(integerConversion && precision is not null);
            output.Append(Pad(text, width, left, padZero));
        }
        return output.ToString();
    }

    private static string? Convert(char conversion, double arg, int? precision, bool plus, bool space, bool alt)
    {
        string Sign(double v, string digits) => v < 0 ? "-" + digits : plus ? "+" + digits : space ? " " + digits : digits;
        switch (conversion)
        {
            case 'd':
            case 'i':
            {
                var v = (long)Math.Truncate(arg);
                var digits = Math.Abs(v).ToString(CultureInfo.InvariantCulture);
                if (precision is { } p && digits.Length < p) digits = digits.PadLeft(p, '0');
                return Sign(v, digits);
            }
            case 'u':
                return ((uint)(long)Math.Truncate(arg)).ToString(CultureInfo.InvariantCulture);
            case 'x':
            case 'X':
            case 'o':
            {
                var v = (uint)(long)Math.Truncate(arg);
                var digits = conversion == 'o' ? System.Convert.ToString(v, 8)
                    : v.ToString(conversion == 'x' ? "x" : "X", CultureInfo.InvariantCulture);
                if (alt && v != 0) digits = (conversion == 'o' ? "0" : conversion == 'x' ? "0x" : "0X") + digits;
                return digits;
            }
            case 'c':
                return ((char)(long)arg).ToString();
            case 's':
                return string.Empty;
            case 'f':
            case 'F':
                return Sign(arg, Math.Abs(arg).ToString("F" + (precision ?? 6), CultureInfo.InvariantCulture));
            case 'e':
            case 'E':
            {
                var s = Math.Abs(arg).ToString((conversion == 'e' ? "e" : "E") + (precision ?? 6), CultureInfo.InvariantCulture);
                // C prints at least two exponent digits; .NET prints three.
                var at = s.IndexOfAny(new[] { 'e', 'E' });
                if (at >= 0 && s.Length - at - 2 == 3 && s[at + 2] == '0') s = s.Remove(at + 2, 1);
                return Sign(arg, s);
            }
            case 'g':
            case 'G':
            {
                var p = precision ?? 6;
                if (p == 0) p = 1;
                var s = Math.Abs(arg).ToString((conversion == 'g' ? "g" : "G") + p, CultureInfo.InvariantCulture);
                return Sign(arg, s);
            }
            default:
                return null;
        }
    }

    private static string Pad(string text, int width, bool left, bool zero)
    {
        if (text.Length >= width) return text;
        if (left) return text.PadRight(width);
        if (!zero) return text.PadLeft(width);
        var signed = text.Length > 0 && text[0] is '-' or '+' or ' ';
        return signed ? text[0] + text[1..].PadLeft(width - 1, '0') : text.PadLeft(width, '0');
    }
}
