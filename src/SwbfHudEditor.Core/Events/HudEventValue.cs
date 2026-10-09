using System.Globalization;
using System.Numerics;

namespace SwbfHudEditor.Core.Events;

/// <summary><c>HUD::EventClass::Type</c>, in engine order.</summary>
public enum HudEventType
{
    Invalid = 0,
    Bool = 1,
    Int = 2,
    Uint = 3,
    Float = 4,
    Model = 5,
    Texture = 6,
    Color = 7,
    String = 8,
    Vector3 = 9,
}

/// <summary>An RGBA colour as the HUD sends it.</summary>
public readonly record struct HudColor(byte R, byte G, byte B, byte A = 255)
{
    public static readonly HudColor White = new(255, 255, 255);

    public override string ToString() => $"{R}, {G}, {B}";
}

/// <summary>
/// What an event carries (<c>HUD::Event::mData</c>). A Uint that is a name hash keeps the name it was
/// made from, so the preview can show a texture or mesh by name; a Model or Texture is a name.
/// </summary>
public sealed record HudEventValue
{
    public required HudEventType Type { get; init; }
    public bool Bool { get; init; }
    public int Int { get; init; }
    public uint Uint { get; init; }
    public float Float { get; init; }
    public HudColor Color { get; init; } = HudColor.White;
    public Vector3 Vector { get; init; }

    /// <summary>A String's text, a Model's or Texture's name, or the name a Uint hash was made from.</summary>
    public string? Text { get; init; }

    public static HudEventValue FromBool(bool v) => new() { Type = HudEventType.Bool, Bool = v };
    public static HudEventValue FromInt(int v) => new() { Type = HudEventType.Int, Int = v };
    public static HudEventValue FromUint(uint v) => new() { Type = HudEventType.Uint, Uint = v };
    public static HudEventValue FromName(string name) => new() { Type = HudEventType.Uint, Uint = PblHash.Of(name), Text = name };
    public static HudEventValue FromFloat(float v) => new() { Type = HudEventType.Float, Float = v };
    public static HudEventValue FromColor(HudColor c) => new() { Type = HudEventType.Color, Color = c };
    public static HudEventValue FromString(string s) => new() { Type = HudEventType.String, Text = s };
    public static HudEventValue FromVector(Vector3 v) => new() { Type = HudEventType.Vector3, Vector = v };
    public static HudEventValue FromModel(string name) => new() { Type = HudEventType.Model, Text = name };
    public static HudEventValue FromTexture(string name) => new() { Type = HudEventType.Texture, Text = name };

    public bool IsNumber => Type is HudEventType.Int or HudEventType.Uint or HudEventType.Float;

    /// <summary><c>Event::GetDataFloat</c>: Int and Uint as floats, Float as is, anything else 0.</summary>
    public float AsFloat() => Type switch
    {
        HudEventType.Int => Int,
        HudEventType.Uint => Uint,
        HudEventType.Float => Float,
        _ => 0f,
    };

    /// <summary>A sensible value to send for a type when the user has not set one.</summary>
    public static HudEventValue Default(HudEventType type) => type switch
    {
        HudEventType.Bool => FromBool(true),
        HudEventType.Int => FromInt(1),
        HudEventType.Uint => FromUint(1),
        HudEventType.Float => FromFloat(1f),
        HudEventType.Color => FromColor(HudColor.White),
        HudEventType.String => FromString("Text"),
        HudEventType.Vector3 => FromVector(new Vector3(0.5f, 0.5f, 0f)),
        HudEventType.Model => FromModel(string.Empty),
        HudEventType.Texture => FromTexture(string.Empty),
        _ => FromBool(true),
    };

    public override string ToString() => Type switch
    {
        HudEventType.Bool => Bool ? "true" : "false",
        HudEventType.Int => Int.ToString(CultureInfo.InvariantCulture),
        HudEventType.Uint => Text is { Length: > 0 } name ? $"\"{name}\"" : Uint.ToString(CultureInfo.InvariantCulture),
        HudEventType.Float => Float.ToString("0.###", CultureInfo.InvariantCulture),
        HudEventType.Color => Color.ToString(),
        HudEventType.String => $"\"{Text}\"",
        HudEventType.Vector3 => string.Create(CultureInfo.InvariantCulture, $"{Vector.X:0.###}, {Vector.Y:0.###}, {Vector.Z:0.###}"),
        HudEventType.Model or HudEventType.Texture => Text ?? string.Empty,
        _ => string.Empty,
    };
}

/// <summary>
/// <c>PblHash</c>: 32-bit FNV-1a over the name with each byte OR 0x20, so names compare without case
/// (BF2GameExt <c>docs/RE/HUDEventBindings.md</c>). Event names, texture names and model names are all
/// found by it.
/// </summary>
public static class PblHash
{
    public static uint Of(string text)
    {
        var hash = 0x811C9DC5u;
        foreach (var c in text)
        {
            hash ^= (byte)(c | 0x20);
            hash *= 0x01000193u;
        }
        return hash;
    }
}
