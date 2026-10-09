using System.Globalization;

namespace SwbfHudEditor.Core.Hud;

public enum HudArgKind
{
    String,
    Integer,
    Float,
    Identifier,
}

public sealed record HudArgument(HudArgKind Kind, string Raw)
{
    public static HudArgument FromString(string value) => new(HudArgKind.String, value);
    public static HudArgument FromIdentifier(string value) => new(HudArgKind.Identifier, value);
    public static HudArgument FromInt(long value) => new(HudArgKind.Integer, value.ToString(CultureInfo.InvariantCulture));
    public static HudArgument FromFloat(double value) => new(HudArgKind.Float, value.ToString("0.000000", CultureInfo.InvariantCulture));

    public long AsInt() => long.Parse(Raw, CultureInfo.InvariantCulture);
    public double AsDouble() => double.Parse(Raw, CultureInfo.InvariantCulture);
    public string AsString() => Raw;
}
