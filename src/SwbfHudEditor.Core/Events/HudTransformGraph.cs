using System.Numerics;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Events;

/// <summary>
/// The transforms of the loaded <c>.hud</c> files, in declaration order, each turning the latest value
/// of its inputs into its outputs. The four native classes follow the game's code (Phantom
/// <c>TransformNumberColor::CalculateOutputColor</c> <c>0061D1D0</c>,
/// <c>TransformNumberType::CalculateInputValue</c> <c>0061DE70</c>,
/// <c>TransformNumberVector3::CalculateOutputVector3</c> <c>0061E260</c>,
/// <c>TransformNumberColorBlend::EventInput</c> <c>0061D940</c>, <c>TransformNameMesh::EventInput</c>
/// <c>0061C8E0</c>); Math, Lerp and Compare follow BF2GameExt's <c>docs/RE/HUDAuthoring.md</c>.
/// </summary>
/// <remarks>
/// The preview has no clock: a Lerp has reached its input, and a Compare's hysteresis is not applied.
/// </remarks>
public sealed class HudTransformGraph
{
    public IReadOnlyList<TransformNode> Nodes { get; }

    private HudTransformGraph(IReadOnlyList<TransformNode> nodes) => Nodes = nodes;

    /// <summary>Every transform in the documents, in order, names filtered as their ViewPort reads them.</summary>
    public static HudTransformGraph From(IEnumerable<HudDocument> documents)
    {
        var nodes = new List<TransformNode>();
        foreach (var doc in documents)
            Collect(doc.TopLevel, null, nodes);
        var byName = new Dictionary<string, NameMeshNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes.OfType<NameMeshNode>())
        {
            n.Resolve(byName);
            if (n.InstanceName is { } name) byName.TryAdd(name, n);
        }
        return new HudTransformGraph(nodes);
    }

    private static void Collect(IEnumerable<HudBlock> blocks, string? filter, List<TransformNode> into)
    {
        foreach (var block in blocks)
        {
            if (!block.HasBody) continue;
            if (block.Name.Equals("ViewPort", StringComparison.OrdinalIgnoreCase))
            {
                // ViewPort::Read reads its scope once per camera with its EventNameFilter set.
                var scopeFilter = block.Children.LastOrDefault(c => !c.HasBody && c.Name == "EventNameFilter" &&
                                                                   c.Arguments.Count > 0)?.Arguments[0].Raw;
                Collect(block.Children, scopeFilter, into);
                continue;
            }
            var node = TransformNode.Read(block, name => EventNames.Filter(name, filter));
            if (node is not null) into.Add(node);
        }
    }

    /// <summary>The events every transform reads, so the editor can list them.</summary>
    public IEnumerable<string> Inputs => Nodes.SelectMany(n => n.Inputs);

    /// <summary>The events transforms publish, with their types.</summary>
    public IEnumerable<(string Name, HudEventType Type, TransformNode From)> Outputs =>
        Nodes.SelectMany(n => n.OutputTypes.Select(o => (o.Name, o.Type, n)));
}

/// <summary><c>HUD::Item::GetFilteredEventName</c>, as the ViewPort's filter renames player events.</summary>
public static class EventNames
{
    /// <summary>
    /// Walks the name and the filter together; where they part at the filter's <c>%</c>, the camera's
    /// number (1 on PC) replaces any digits in the name. With no filter the name is unchanged.
    /// </summary>
    public static string Filter(string name, string? filter, int index = 1)
    {
        if (string.IsNullOrEmpty(filter)) return name;
        var i = 0;
        while (i < name.Length && i < filter.Length && char.ToLowerInvariant(name[i]) == char.ToLowerInvariant(filter[i])) i++;
        if (i >= filter.Length || filter[i] != '%') return name;
        var rest = i;
        while (rest < name.Length && char.IsDigit(name[rest])) rest++;
        return name[..i] + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + name[rest..];
    }
}

/// <summary>One transform.</summary>
public abstract class TransformNode
{
    public required string Kind { get; init; }
    public required string? InstanceName { get; init; }
    public required HudBlock Block { get; init; }

    /// <summary>A configuration the game would refuse (GameExt's transforms switch off on one).</summary>
    public string? Problem { get; protected set; }

    public abstract IEnumerable<string> Inputs { get; }

    /// <summary>The events that make it publish; the rest only update what it holds.</summary>
    public abstract IEnumerable<string> Triggers { get; }

    public abstract IEnumerable<(string Name, HudEventType Type)> OutputTypes { get; }

    /// <summary>What it publishes for the latest values, or nothing when it would send nothing.</summary>
    public abstract IEnumerable<(string Name, HudEventValue Value)> Run(Func<string, HudEventValue?> latest);

    public static TransformNode? Read(HudBlock block, Func<string, string> filter)
    {
        var name = block.Arguments.Count > 0 ? block.Arguments[0].Raw : null;
        return block.Name switch
        {
            "TransformNumberColor" => MappedNode.Read(block, name, filter, false),
            "TransformNumberVector3" => MappedNode.Read(block, name, filter, true),
            "TransformNumberColorBlend" => ColorBlendNode.Read(block, name, filter),
            "TransformNameMesh" => NameMeshNode.Read(block, name, filter),
            "TransformNumberMath" => OperandNode.Read(block, name, filter, OperandKind.Math),
            "TransformNumberLerp" => LerpNode.Read(block, name, filter),
            "TransformNumberCompare" => OperandNode.Read(block, name, filter, OperandKind.Compare),
            _ => null,
        };
    }

    protected static IEnumerable<HudBlock> Lines(HudBlock block) => block.Children.Where(c => !c.HasBody);

    protected static string? Str(HudBlock line, int i = 0) =>
        i < line.Arguments.Count && line.Arguments[i].Kind == HudArgKind.String ? line.Arguments[i].Raw : null;

    protected static float Num(HudBlock line, int i = 0) => i < line.Arguments.Count ? EngineArgs.Float(line.Arguments[i]) : 0f;

    protected static bool IsNum(HudBlock line, int i = 0) => i < line.Arguments.Count && EngineArgs.IsNumber(line.Arguments[i]);
}

/// <summary>TransformNumberColor and TransformNumberVector3: a numeric input looked up in a table.</summary>
public sealed class MappedNode : TransformNode
{
    public string? Input { get; private set; }
    public string? Output { get; private set; }
    public string? FactorInput { get; private set; }
    public float Factor { get; private set; } = 1f;
    public bool Wrap { get; private set; }
    public bool IsVector { get; private init; }
    public List<(float Key, HudColor Color)> Colors { get; } = new();
    public List<(float Key, Vector3 Vector)> Vectors { get; } = new();

    public static MappedNode Read(HudBlock block, string? name, Func<string, string> filter, bool vector)
    {
        var n = new MappedNode { Kind = block.Name, InstanceName = name, Block = block, IsVector = vector };
        foreach (var line in Lines(block))
        {
            switch (line.Name)
            {
                case "EventInput": n.Input = Str(line) is { } i ? filter(i) : null; break;
                case "EventOutput": n.Output = Str(line) is { } o ? filter(o) : null; break;
                case "EventInputFactor": n.FactorInput = Str(line) is { } f ? filter(f) : null; break;
                case "InputFactor": n.Factor = Num(line); break;
                case "WrapInput": n.Wrap = Num(line) != 0f; break;
                case "NumberColor" when !vector && line.Arguments.Count >= 4:
                    n.Colors.Add((Num(line), new HudColor(Byte(Num(line, 1)), Byte(Num(line, 2)), Byte(Num(line, 3)))));
                    break;
                case "NumberVector3" when vector && line.Arguments.Count >= 4:
                    n.Vectors.Add((Num(line), new Vector3(Num(line, 1), Num(line, 2), Num(line, 3))));
                    break;
            }
        }
        return n;
    }

    private static byte Byte(float v) => (byte)(int)v;

    public override IEnumerable<string> Inputs => new[] { Input, FactorInput }.OfType<string>();
    public override IEnumerable<string> Triggers => new[] { Input }.OfType<string>();

    public override IEnumerable<(string Name, HudEventType Type)> OutputTypes =>
        Output is null ? Array.Empty<(string, HudEventType)>() : new[] { (Output, IsVector ? HudEventType.Vector3 : HudEventType.Color) };

    public override IEnumerable<(string Name, HudEventValue Value)> Run(Func<string, HudEventValue?> latest)
    {
        if (Output is null || Input is null || latest(Input) is not { IsNumber: true } input) yield break;
        var count = IsVector ? Vectors.Count : Colors.Count;
        if (count == 0) yield break;
        // The factor event's latest value, 1 until it has sent one.
        var factorValue = FactorInput is not null && latest(FactorInput) is { IsNumber: true } f ? f.AsFloat() : 1f;
        var x = input.AsFloat() * Factor * factorValue;
        yield return IsVector ? (Output, HudEventValue.FromVector(LookUpVector(x))) : (Output, HudEventValue.FromColor(LookUpColor(x)));
    }

    /// <summary><c>CalculateInputValue</c>: clamp to the table, or the native wrap.</summary>
    public static float InputValue(float x, float first, float last, bool wrap)
    {
        if (!wrap) return first <= x ? (x <= last ? x : last) : first;
        if (x < first)
        {
            x = first - x;
            if (x < first) x = last - x;
        }
        if (last < x) return x - (int)(x / (last - first)) * (last - first) + first;
        return x;
    }

    /// <summary>The first row with a key at or above x, and the one before it.</summary>
    private static (int Lo, int Hi) Bracket(int count, Func<int, float> key, float x)
    {
        var i = 0;
        while (i < count && !(x <= key(i))) i++;
        var hi = i == count ? i - 1 : i;
        var lo = i != 0 ? i - 1 : 0;
        return (lo, hi);
    }

    public HudColor LookUpColor(float raw)
    {
        var x = InputValue(raw, Colors[0].Key, Colors[^1].Key, Wrap);
        var (lo, hi) = Bracket(Colors.Count, i => Colors[i].Key, x);
        var a = Colors[lo];
        var b = Colors[hi];
        var span = b.Key - a.Key;
        if (span == 0f) return b.Color;
        var t = (x - a.Key) / span;
        var u = 1f - t;
        static byte Mix(byte p, byte q, float u, float t) => (byte)(int)(p * u + q * t + 0.5f);
        return new HudColor(Mix(a.Color.R, b.Color.R, u, t), Mix(a.Color.G, b.Color.G, u, t),
            Mix(a.Color.B, b.Color.B, u, t), Mix(a.Color.A, b.Color.A, u, t));
    }

    public Vector3 LookUpVector(float raw)
    {
        var x = InputValue(raw, Vectors[0].Key, Vectors[^1].Key, Wrap);
        var (lo, hi) = Bracket(Vectors.Count, i => Vectors[i].Key, x);
        var a = Vectors[lo];
        var b = Vectors[hi];
        var span = b.Key - a.Key;
        if (span == 0f) return b.Vector;
        var t = (x - a.Key) / span;
        return (b.Vector - a.Vector) * t + a.Vector;
    }
}

/// <summary>
/// TransformNumberColorBlend: the mapped colour blended with the latest <c>EventBlend</c> colour by a
/// weight byte, <c>Alpha</c> 127 by default.
/// </summary>
public sealed class ColorBlendNode : TransformNode
{
    public MappedNode Map { get; private init; } = null!;
    public string? Blend { get; private set; }
    public string? AlphaInput { get; private set; }
    public bool Additive { get; private set; }
    public byte Weight { get; private set; } = 127;

    public static ColorBlendNode Read(HudBlock block, string? name, Func<string, string> filter)
    {
        var n = new ColorBlendNode { Kind = block.Name, InstanceName = name, Block = block, Map = MappedNode.Read(block, name, filter, false) };
        foreach (var line in Lines(block))
        {
            switch (line.Name)
            {
                case "EventBlend": n.Blend = Str(line) is { } b ? filter(b) : null; break;
                case "EventAlpha": n.AlphaInput = Str(line) is { } a ? filter(a) : null; break;
                case "BlendMode": n.Additive = string.Equals(Str(line), "Additive", StringComparison.OrdinalIgnoreCase); break;
                case "Alpha": n.Weight = (byte)(int)(Num(line) * 255f); break;
            }
        }
        return n;
    }

    public override IEnumerable<string> Inputs => new[] { Map.Input, Blend, AlphaInput }.OfType<string>();
    public override IEnumerable<string> Triggers => new[] { Map.Input }.OfType<string>();

    public override IEnumerable<(string Name, HudEventType Type)> OutputTypes =>
        Map.Output is null ? Array.Empty<(string, HudEventType)>() : new[] { (Map.Output, HudEventType.Color) };

    public override IEnumerable<(string Name, HudEventValue Value)> Run(Func<string, HudEventValue?> latest)
    {
        if (Map.Output is null || Map.Input is null || latest(Map.Input) is not { IsNumber: true } input || Map.Colors.Count == 0)
            yield break;
        // The reference handler looks the raw input up: no factors.
        var m = Map.LookUpColor(input.AsFloat());
        var b = Blend is not null && latest(Blend) is { Type: HudEventType.Color } blend ? blend.Color : new HudColor(0, 0, 0, 0);
        int a = Weight;
        if (AlphaInput is not null && latest(AlphaInput) is { IsNumber: true } w)
            a = w.Type == HudEventType.Float ? (byte)(int)(w.Float * 255f) : (byte)(w.Type == HudEventType.Int ? w.Int : (int)w.Uint);
        HudColor result;
        if (!Additive)
        {
            static byte Mix(int a, byte m, byte b) => (byte)((a * m >> 8) + ((255 - a) * b >> 8));
            result = new HudColor(Mix(a, m.R, b.R), Mix(a, m.G, b.G), Mix(a, m.B, b.B), m.A);
        }
        else
        {
            static byte Add(int a, byte m, byte b) => (byte)Math.Min(255, (a * m >> 8) + b);
            result = new HudColor(Add(a, m.R, b.R), Add(a, m.G, b.G), Add(a, m.B, b.B), m.A);
        }
        yield return (Map.Output, HudEventValue.FromColor(result));
    }
}

/// <summary>
/// TransformNameMesh: a Uint name hash to a model, by its <c>NameMesh(source, mesh)</c> rows and any
/// it inherits; with no row, the model of that name if one is loaded.
/// </summary>
public sealed class NameMeshNode : TransformNode
{
    public string? Input { get; private set; }
    public string? Output { get; private set; }
    public List<(string Source, string Mesh)> Rows { get; } = new();
    private readonly List<string> _inherits = new();

    public static NameMeshNode Read(HudBlock block, string? name, Func<string, string> filter)
    {
        var n = new NameMeshNode { Kind = block.Name, InstanceName = name, Block = block };
        foreach (var line in Lines(block))
        {
            switch (line.Name)
            {
                case "EventInput": n.Input = Str(line) is { } i ? filter(i) : null; break;
                case "EventOutput": n.Output = Str(line) is { } o ? filter(o) : null; break;
                case "NameMesh" when Str(line, 0) is { } src && Str(line, 1) is { } mesh: n.Rows.Add((src, mesh)); break;
                case "TransformNameMesh" when Str(line) is { } other: n._inherits.Add(other); break;
            }
        }
        return n;
    }

    internal void Resolve(Dictionary<string, NameMeshNode> earlier)
    {
        foreach (var other in _inherits)
            if (earlier.TryGetValue(other, out var from)) Rows.AddRange(from.Rows);
    }

    public override IEnumerable<string> Inputs => new[] { Input }.OfType<string>();
    public override IEnumerable<string> Triggers => Inputs;

    public override IEnumerable<(string Name, HudEventType Type)> OutputTypes =>
        Output is null ? Array.Empty<(string, HudEventType)>() : new[] { (Output, HudEventType.Model) };

    public override IEnumerable<(string Name, HudEventValue Value)> Run(Func<string, HudEventValue?> latest)
    {
        if (Output is null || Input is null || latest(Input) is not { Type: HudEventType.Uint } input || input.Uint == 0) yield break;
        foreach (var (source, mesh) in Rows)
            if (PblHash.Of(source) == input.Uint)
            {
                yield return (Output, HudEventValue.FromModel(mesh));
                yield break;
            }
        // No row: the model with that name hash, if the name is known.
        if (input.Text is { Length: > 0 } name) yield return (Output, HudEventValue.FromModel(name));
    }
}

public enum OperandKind { Math, Compare }

/// <summary>An operand: a constant or the latest value of a numeric event.</summary>
public readonly record struct Operand(float? Constant, string? Event)
{
    public double? Value(Func<string, HudEventValue?> latest) =>
        Constant is { } c ? c : Event is not null && latest(Event) is { IsNumber: true } v ? ValueOf(v) : null;

    /// <summary>Comparisons and arithmetic use the value as sent, in double precision.</summary>
    public static double ValueOf(HudEventValue v) => v.Type switch
    {
        HudEventType.Int => v.Int,
        HudEventType.Uint => v.Uint,
        _ => v.Float,
    };
}

/// <summary>GameExt's TransformNumberMath and TransformNumberCompare: A and B, each a constant or an event.</summary>
public sealed class OperandNode : TransformNode
{
    public OperandKind Mode { get; private init; }
    public string? Operation { get; private set; }
    public Operand A { get; private set; }
    public Operand B { get; private set; }
    public (float Min, float Max)? Clamp { get; private set; }
    public string? Output { get; private set; }
    public string? OutputTrue { get; private set; }
    public string? OutputFalse { get; private set; }

    /// <summary>Compare's <c>Hysteresis</c>: once on, it stays on until the inputs are this far the other way.</summary>
    public float Hysteresis { get; private set; }

    /// <summary><c>OutputIsAlpha(1)</c>: the output is sent again every frame (an <c>EventAlpha</c> lasts one).</summary>
    public bool OutputIsAlpha { get; private set; }

    private static readonly string[] MathOps = { "Add", "Subtract", "Multiply", "Divide", "Min", "Max" };
    private static readonly string[] CompareOps = { "Greater", "GreaterOrEqual", "Less", "LessOrEqual", "Equal", "NotEqual" };

    public static OperandNode Read(HudBlock block, string? name, Func<string, string> filter, OperandKind mode)
    {
        var n = new OperandNode { Kind = block.Name, InstanceName = name, Block = block, Mode = mode };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Operand? a = null, b = null;
        foreach (var line in Lines(block))
        {
            var key = line.Name is "ConstantA" or "EventInputA" ? "A" : line.Name is "ConstantB" or "EventInputB" ? "B" : line.Name;
            if (!seen.Add(key))
            {
                n.Problem ??= $"{line.Name} repeats a setting";
                continue;
            }
            switch (line.Name)
            {
                case "Operation":
                    n.Operation = (mode == OperandKind.Math ? MathOps : CompareOps)
                        .FirstOrDefault(o => string.Equals(o, Str(line), StringComparison.OrdinalIgnoreCase));
                    if (n.Operation is null) n.Problem ??= "unknown Operation";
                    break;
                case "ConstantA" when IsNum(line): a = new Operand(Num(line), null); break;
                case "ConstantB" when IsNum(line): b = new Operand(Num(line), null); break;
                case "EventInputA" when Str(line) is { } e: a = new Operand(null, filter(e)); break;
                case "EventInputB" when Str(line) is { } e: b = new Operand(null, filter(e)); break;
                case "Clamp" when IsNum(line, 0) && IsNum(line, 1) && Num(line) <= Num(line, 1): n.Clamp = (Num(line), Num(line, 1)); break;
                case "EventOutput" when Str(line) is { } o: n.Output = filter(o); break;
                case "EventOutputTrue" when Str(line) is { } o: n.OutputTrue = filter(o); break;
                case "EventOutputFalse" when Str(line) is { } o: n.OutputFalse = filter(o); break;
                case "Hysteresis" when IsNum(line) && Num(line) >= 0f: n.Hysteresis = Num(line); break;
                case "OutputIsAlpha" when IsNum(line): n.OutputIsAlpha = Num(line) != 0f; break;
            }
        }
        if (a is null || b is null) n.Problem ??= "needs both A and B";
        if (n.Operation is null) n.Problem ??= "needs an Operation";
        if (n.Output is null && n.OutputTrue is null && n.OutputFalse is null) n.Problem ??= "needs an EventOutput";
        n.A = a ?? default;
        n.B = b ?? default;
        return n;
    }

    public override IEnumerable<string> Inputs => new[] { A.Event, B.Event }.OfType<string>();
    public override IEnumerable<string> Triggers => Inputs;

    public override IEnumerable<(string Name, HudEventType Type)> OutputTypes
    {
        get
        {
            if (Output is not null) yield return (Output, HudEventType.Float);
            if (OutputTrue is not null) yield return (OutputTrue, HudEventType.Bool);
            if (OutputFalse is not null) yield return (OutputFalse, HudEventType.Bool);
        }
    }

    public override IEnumerable<(string Name, HudEventValue Value)> Run(Func<string, HudEventValue?> latest)
    {
        if (Problem is not null || A.Value(latest) is not { } a || B.Value(latest) is not { } b) yield break;
        if (Mode == OperandKind.Math)
        {
            double r = Operation switch
            {
                "Add" => a + b,
                "Subtract" => a - b,
                "Multiply" => a * b,
                "Divide" => b == 0 ? double.NaN : a / b,
                "Min" => Math.Min(a, b),
                _ => Math.Max(a, b),
            };
            if (Clamp is { } c && double.IsFinite(r)) r = Math.Clamp(r, c.Min, c.Max);
            // Division by zero, non-finite results and results past a float send nothing.
            if (!double.IsFinite(r) || Math.Abs(r) > float.MaxValue) yield break;
            if (Output is not null) yield return (Output, HudEventValue.FromFloat((float)r));
            yield break;
        }
        var on = Operation switch
        {
            "Greater" => a > b,
            "GreaterOrEqual" => a >= b,
            "Less" => a < b,
            "LessOrEqual" => a <= b,
            "Equal" => Math.Abs(a - b) <= Hysteresis,
            _ => Math.Abs(a - b) > Hysteresis,
        };
        if (Output is not null) yield return (Output, HudEventValue.FromFloat(on ? 1f : 0f));
        if (on && OutputTrue is not null) yield return (OutputTrue, HudEventValue.FromBool(true));
        if (!on && OutputFalse is not null) yield return (OutputFalse, HudEventValue.FromBool(true));
    }
}

/// <summary>GameExt's TransformNumberLerp, shown as it ends up: A + (B - A) × s, s the input placed in its range.</summary>
public sealed class LerpNode : TransformNode
{
    public string? Input { get; private set; }
    public Operand RangeMin { get; private set; } = new(0f, null);
    public Operand RangeMax { get; private set; } = new(1f, null);
    public Operand A { get; private set; } = new(0f, null);
    public Operand B { get; private set; } = new(1f, null);
    public string? Output { get; private set; }

    /// <summary>Seconds a full 0 to 1 swing of the input takes going up, and going down; 0 moves at once.</summary>
    public float RiseTime { get; private set; }
    public float FallTime { get; private set; }

    /// <summary><c>OutputIsAlpha(1)</c>: the output is sent again every frame.</summary>
    public bool OutputIsAlpha { get; private set; }

    public static LerpNode Read(HudBlock block, string? name, Func<string, string> filter)
    {
        var n = new LerpNode { Kind = block.Name, InstanceName = name, Block = block };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in Lines(block))
        {
            var key = line.Name is "ConstantA" or "EventInputA" ? "A" : line.Name is "ConstantB" or "EventInputB" ? "B" : line.Name;
            if (!seen.Add(key))
            {
                n.Problem ??= $"{line.Name} repeats a setting";
                continue;
            }
            switch (line.Name)
            {
                case "EventInput" when Str(line) is { } i: n.Input = filter(i); break;
                case "EventOutput" when Str(line) is { } o: n.Output = filter(o); break;
                case "ConstantA" when IsNum(line): n.A = new Operand(Num(line), null); break;
                case "ConstantB" when IsNum(line): n.B = new Operand(Num(line), null); break;
                case "EventInputA" when Str(line) is { } e: n.A = new Operand(null, filter(e)); break;
                case "EventInputB" when Str(line) is { } e: n.B = new Operand(null, filter(e)); break;
                case "RiseTime" when IsNum(line) && Num(line) >= 0f: n.RiseTime = Num(line); break;
                case "FallTime" when IsNum(line) && Num(line) >= 0f: n.FallTime = Num(line); break;
                case "OutputIsAlpha" when IsNum(line): n.OutputIsAlpha = Num(line) != 0f; break;
                case "InputRange" when line.Arguments.Count == 2:
                    n.RangeMin = IsNum(line, 0) ? new Operand(Num(line, 0), null) : new Operand(null, Str(line, 0) is { } lo ? filter(lo) : null);
                    n.RangeMax = IsNum(line, 1) ? new Operand(Num(line, 1), null) : new Operand(null, Str(line, 1) is { } hi ? filter(hi) : null);
                    break;
            }
        }
        if (n.Input is null) n.Problem ??= "needs an EventInput";
        if (n.Output is null) n.Problem ??= "needs an EventOutput";
        return n;
    }

    public override IEnumerable<string> Inputs => new[] { Input, A.Event, B.Event, RangeMin.Event, RangeMax.Event }.OfType<string>();
    public override IEnumerable<string> Triggers => Inputs;

    public override IEnumerable<(string Name, HudEventType Type)> OutputTypes =>
        Output is null ? Array.Empty<(string, HudEventType)>() : new[] { (Output, HudEventType.Float) };

    public override IEnumerable<(string Name, HudEventValue Value)> Run(Func<string, HudEventValue?> latest)
    {
        if (Problem is not null || Input is null || latest(Input) is not { IsNumber: true } input) yield break;
        if (A.Value(latest) is not { } a || B.Value(latest) is not { } b) yield break;
        if (RangeMin.Value(latest) is not { } lo || RangeMax.Value(latest) is not { } hi || lo == hi) yield break;
        var s = Math.Clamp((Operand.ValueOf(input) - lo) / (hi - lo), 0, 1);
        var r = a + (b - a) * s;
        if (!double.IsFinite(r)) yield break;
        yield return (Output!, HudEventValue.FromFloat((float)r));
    }
}
