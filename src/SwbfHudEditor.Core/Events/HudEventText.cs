using System.Globalization;
using System.Numerics;

namespace SwbfHudEditor.Core.Events;

/// <summary>Event values as the editor types them: one line of text per value.</summary>
public static class HudEventText
{
    /// <summary>
    /// Bool <c>true</c>/<c>false</c>/<c>1</c>/<c>0</c>; Int; Uint as a number, <c>0x</c> hex or a name
    /// (sent as its hash, as a weapon change or a texture is); Float, with <c>max</c> for FLT_MAX; Color
    /// <c>r, g, b</c> or <c>#RRGGBB</c>; Vector3 <c>x, y, z</c>; String, Model and Texture as written.
    /// </summary>
    public static bool TryParse(HudEventType type, string? text, out HudEventValue value)
    {
        value = HudEventValue.Default(type);
        var t = (text ?? string.Empty).Trim();
        switch (type)
        {
            case HudEventType.Bool:
                if (t.Equals("true", StringComparison.OrdinalIgnoreCase) || t == "1") { value = HudEventValue.FromBool(true); return true; }
                if (t.Equals("false", StringComparison.OrdinalIgnoreCase) || t == "0") { value = HudEventValue.FromBool(false); return true; }
                return false;
            case HudEventType.Int:
                if (!int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return false;
                value = HudEventValue.FromInt(i);
                return true;
            case HudEventType.Uint:
                if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                    uint.TryParse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                {
                    value = HudEventValue.FromUint(hex);
                    return true;
                }
                if (uint.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u))
                {
                    value = HudEventValue.FromUint(u);
                    return true;
                }
                if (t.Length == 0) return false;
                value = HudEventValue.FromName(t);
                return true;
            case HudEventType.Float:
                if (t.Equals("max", StringComparison.OrdinalIgnoreCase) || t.Equals("inf", StringComparison.OrdinalIgnoreCase))
                {
                    value = HudEventValue.FromFloat(float.MaxValue);
                    return true;
                }
                if (!float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return false;
                value = HudEventValue.FromFloat(f);
                return true;
            case HudEventType.Color:
                if (t.StartsWith('#') && t.Length == 7 &&
                    uint.TryParse(t[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                {
                    value = HudEventValue.FromColor(new HudColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
                    return true;
                }
                var parts = Numbers(t);
                if (parts is not { Length: 3 or 4 } || parts.Any(p => p is < 0 or > 255)) return false;
                value = HudEventValue.FromColor(new HudColor((byte)parts[0], (byte)parts[1], (byte)parts[2], parts.Length == 4 ? (byte)parts[3] : (byte)255));
                return true;
            case HudEventType.Vector3:
                var v = Numbers(t);
                if (v is not { Length: 3 }) return false;
                value = HudEventValue.FromVector(new Vector3(v[0], v[1], v[2]));
                return true;
            case HudEventType.String:
                value = HudEventValue.FromString(text ?? string.Empty);
                return true;
            case HudEventType.Model:
                value = HudEventValue.FromModel(t);
                return true;
            case HudEventType.Texture:
                value = HudEventValue.FromTexture(t);
                return true;
            default:
                return false;
        }
    }

    /// <summary>A value as the editor shows it for typing again.</summary>
    public static string Format(HudEventValue value) => value.Type switch
    {
        HudEventType.Uint => value.Text is { Length: > 0 } name ? name : value.Uint.ToString(CultureInfo.InvariantCulture),
        HudEventType.Float => value.Float == float.MaxValue ? "max" : value.Float.ToString("0.####", CultureInfo.InvariantCulture),
        HudEventType.String => value.Text ?? string.Empty,
        _ => value.ToString(),
    };

    private static float[]? Numbers(string text)
    {
        var parts = text.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new float[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i])) return null;
        return result;
    }
}
