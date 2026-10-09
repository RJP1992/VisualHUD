using System.Numerics;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Events;

/// <summary>An event as last sent: its value and when, in the order sends were made.</summary>
public readonly record struct EventSend(string Name, HudEventValue Value, long Seq, TransformNode? From = null);

/// <summary>
/// The events sent so far, the latest of each, in order: the game's state as a scenario. The order
/// matters because an element shows or hides by whichever of its enable and disable events came last.
/// </summary>
public sealed class HudEventState
{
    private readonly Dictionary<string, EventSend> _sends = new(StringComparer.OrdinalIgnoreCase);
    private long _next = 1;

    public IReadOnlyDictionary<string, EventSend> Sends => _sends;

    public event EventHandler? Changed;

    public void Send(string name, HudEventValue value)
    {
        _sends[name] = new EventSend(name, value, _next++);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Several sends, in order, with one change notification.</summary>
    public void SendAll(IEnumerable<(string Name, HudEventValue Value)> sends)
    {
        foreach (var (name, value) in sends) _sends[name] = new EventSend(name, value, _next++);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Forget(string name)
    {
        if (_sends.Remove(name)) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _sends.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public enum GroupMoveKind { Position, Scale, Rotation }

/// <summary>An <c>EventPosition</c>, <c>EventScale</c> or <c>EventRotation</c> a group received.</summary>
public readonly record struct GroupMove(GroupMoveKind Kind, Vector3 Value, string Event, long Seq);

/// <summary>What the events sent did to one element.</summary>
public sealed class ElementRuntime
{
    /// <summary>The later of its <c>EventEnable</c> and <c>EventDisable</c> events was the enable. An element starts disabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>When its enable event was sent, in the order of all sends (0 if never).</summary>
    public long EnableSeq { get; init; }

    /// <summary>The bar's raw value from <c>EventValue</c>.</summary>
    public float? BarValue { get; init; }

    /// <summary>The text from <c>EventText</c> or <c>EventNumber</c>, whichever came last.</summary>
    public string? Text { get; init; }

    public HudColor? Color { get; init; }

    /// <summary>The texture from <c>EventBitmap</c>, by name.</summary>
    public string? Texture { get; init; }

    /// <summary>The model from <c>EventMesh</c>, by name.</summary>
    public string? Mesh { get; init; }

    /// <summary>A group's alpha from <c>EventAlpha</c>.</summary>
    public float? Alpha { get; init; }

    public IReadOnlyList<GroupMove> Moves { get; init; } = Array.Empty<GroupMove>();
}

/// <summary>
/// The events sent, run through the transforms, then through each element's handlers, as the game's
/// <c>Event::Send</c> reaches them (BF2GameExt <c>docs/RE/HUDElementProperties.md</c>).
/// </summary>
public sealed class HudSimulation
{
    public required IReadOnlyDictionary<string, EventSend> Effective { get; init; }
    public required IReadOnlyDictionary<HudBlock, ElementRuntime> Elements { get; init; }

    public ElementRuntime? For(HudBlock block) => Elements.TryGetValue(block, out var r) ? r : null;

    public static HudSimulation Run(HudDocument document, HudTransformGraph transforms, HudEventState state,
        Func<uint, string?>? localizeByHash = null) => Run(new[] { document }, transforms, state, localizeByHash);

    /// <summary>The events' effect on every element of the files a HUD loads: events are global to them all.</summary>
    public static HudSimulation Run(IReadOnlyList<HudDocument> documents, HudTransformGraph transforms, HudEventState state,
        Func<uint, string?>? localizeByHash = null)
    {
        var effective = new Dictionary<string, EventSend>(state.Sends, StringComparer.OrdinalIgnoreCase);
        HudEventValue? Latest(string name) => effective.TryGetValue(name, out var s) ? s.Value : null;

        // Transforms in declaration order: each publishes when one of its triggers has been sent, at
        // that send's place in the order. A later send of an output by hand wins.
        foreach (var node in transforms.Nodes)
        {
            long seq = 0;
            foreach (var trigger in node.Triggers)
                if (effective.TryGetValue(trigger, out var t)) seq = Math.Max(seq, t.Seq);
            if (seq == 0) continue;
            foreach (var (name, value) in node.Run(Latest).ToList())
                if (!effective.TryGetValue(name, out var current) || current.Seq <= seq)
                    effective[name] = new EventSend(name, value, seq, node);
        }

        var elements = new Dictionary<HudBlock, ElementRuntime>(ReferenceEqualityComparer.Instance);
        foreach (var document in documents)
            foreach (var block in HudLayout.TopLevelElements(document))
                Visit(block, effective, localizeByHash, elements);
        return new HudSimulation { Effective = effective, Elements = elements };
    }

    private static void Visit(HudBlock block, Dictionary<string, EventSend> effective, Func<uint, string?>? localizeByHash,
        Dictionary<HudBlock, ElementRuntime> into)
    {
        if (!ElementKinds.TryGet(block, out var kind)) return;
        var spec = ElementReader.Read(block, kind);
        EventSend? Sent(string? name) => name is not null && effective.TryGetValue(name, out var s) ? s : null;

        var enable = Sent(spec.EventEnable)?.Seq ?? 0;
        var disable = Sent(spec.EventDisable)?.Seq ?? 0;

        float? bar = Sent(spec.EventValue) is { Value.IsNumber: true } v ? v.Value.AsFloat() : null;

        string? text = null;
        long textSeq = 0;
        if (kind == ElementKind.Text)
        {
            if (Sent(spec.EventText) is { } t)
            {
                var s = t.Value.Type switch
                {
                    HudEventType.String => t.Value.Text ?? string.Empty,
                    HudEventType.Uint => localizeByHash?.Invoke(t.Value.Uint) ?? t.Value.Text,
                    _ => null,
                };
                if (s is not null) (text, textSeq) = (s, t.Seq);
            }
            if (Sent(spec.EventNumber) is { } n && n.Seq > textSeq && FormatNumber(spec, n.Value) is { } formatted)
                text = formatted;
        }

        HudColor? color = Sent(spec.EventColor) is { Value.Type: HudEventType.Color } c ? c.Value.Color : null;

        string? texture = Sent(spec.EventBitmap) is { } b && b.Value.Type is HudEventType.Uint or HudEventType.Texture
            ? b.Value.Text ?? $"#{b.Value.Uint:X8}"
            : null;

        string? mesh = Sent(spec.EventMesh) is { } m && m.Value.Type is HudEventType.Uint or HudEventType.Model
            ? m.Value.Text ?? $"#{m.Value.Uint:X8}"
            : null;

        float? alpha = Sent(spec.EventAlpha) is { Value.IsNumber: true } a ? a.Value.AsFloat() : null;

        var moves = new List<GroupMove>();
        void Move(GroupMoveKind k, string? name)
        {
            if (Sent(name) is { Value.Type: HudEventType.Vector3 } s) moves.Add(new GroupMove(k, s.Value.Vector, name!, s.Seq));
        }
        Move(GroupMoveKind.Position, spec.EventPosition);
        Move(GroupMoveKind.Scale, spec.EventScale);
        Move(GroupMoveKind.Rotation, spec.EventRotation);
        moves.Sort((x, y) => x.Seq.CompareTo(y.Seq));

        into[block] = new ElementRuntime
        {
            Enabled = enable > disable,
            EnableSeq = enable,
            BarValue = bar,
            Text = text,
            Color = color,
            Texture = texture,
            Mesh = mesh,
            Alpha = alpha,
            Moves = moves,
        };

        foreach (var child in spec.Children) Visit(child, effective, localizeByHash, into);
    }

    /// <summary>
    /// <c>ElementText::EventNumber</c> (P <c>0060BE50</c>): Int and Uint through <c>IntegerFormat</c>
    /// (<c>%d</c>), Float through <c>FloatFormat</c> (<c>%f</c>) or, with <c>Percent</c>, × 100 as
    /// <c>%.0f%</c>; the infinite values print nothing, or <c>--</c> with <c>InfiniteDashes</c>; a
    /// <c>NumberToTime</c> template prints days, hours, minutes and seconds instead.
    /// </summary>
    public static string? FormatNumber(ElementSpec spec, HudEventValue v)
    {
        if (!v.IsNumber) return null;
        if (spec.NumberToTime is { } template) return FormatTime(spec, template, v.AsFloat());
        var dashes = spec.InfiniteDashes ? "--" : string.Empty;
        if (v.Type is HudEventType.Int or HudEventType.Uint)
        {
            var infinite = v.Type == HudEventType.Uint ? v.Uint == 0xFFFFFFFFu : v.Int == 0x7FFFFFFF;
            if (infinite) return dashes;
            // printf reads the 32 bits as an int.
            return CFormat.Format(spec.IntegerFormat ?? "%d", v.Type == HudEventType.Uint ? unchecked((int)v.Uint) : v.Int);
        }
        if (v.Float == float.MaxValue) return dashes;
        var x = spec.Percent ? v.Float * 100f : v.Float;
        var text = CFormat.Format(spec.Percent ? "%.0f" : spec.FloatFormat ?? "%f", x);
        return spec.Percent ? text + "%" : text;
    }

    /// <summary>The template: <c>%d</c> days, <c>%h</c> hours, <c>%m</c> minutes, <c>%s</c> seconds, each with any printf flags between.</summary>
    private static string FormatTime(ElementSpec spec, string template, float value)
    {
        var minutesTotal = MathF.Floor(value / 60f);
        var seconds = value - minutesTotal * 60f;
        var hoursTotal = MathF.Floor(minutesTotal / 60f);
        var minutes = minutesTotal - hoursTotal * 60f;
        var days = MathF.Floor(hoursTotal / 24f);
        var hours = hoursTotal - days * 24f;

        var output = new System.Text.StringBuilder();
        var conversion = new System.Text.StringBuilder();
        foreach (var c in template)
        {
            if (conversion.Length == 0 || conversion.Length > 0x1d)
            {
                if (c == '%') conversion.Clear().Append('%');
                else output.Append(c);
                continue;
            }
            switch (c)
            {
                case 'd': output.Append(CFormat.Format(conversion + "d", (int)days)); break;
                case 'h': output.Append(CFormat.Format(conversion + "d", (int)hours)); break;
                case 'm': output.Append(CFormat.Format(conversion + "d", (int)minutes)); break;
                case 's':
                    output.Append(spec.FloatFormat is { } f ? CFormat.Format(f, seconds)
                        : CFormat.Format(spec.IntegerFormat ?? conversion + "d", (int)seconds));
                    break;
                default:
                    conversion.Append(c);
                    continue;
            }
            conversion.Clear();
        }
        return output.ToString();
    }
}

/// <summary>An event the loaded files use, with what it is and who reads or writes it.</summary>
public sealed record HudEventUse(string Name, HudEventType Type, HudEventSource Source, string? Note,
    IReadOnlyList<string> ReadBy, IReadOnlyList<string> WrittenBy);

public static class HudEventUsage
{
    /// <summary>
    /// Every event the document and the transforms name, typed by the catalogue, else by the transform
    /// that publishes it, else by the property that reads it.
    /// </summary>
    public static IReadOnlyList<HudEventUse> Of(HudDocument document, HudTransformGraph transforms) =>
        Of(new[] { document }, transforms);

    /// <summary>Every event the files of a HUD and their transforms name.</summary>
    public static IReadOnlyList<HudEventUse> Of(IReadOnlyList<HudDocument> documents, HudTransformGraph transforms)
    {
        var readers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var guessed = new Dictionary<string, HudEventType>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        void Read(string name, string by, HudEventType guess)
        {
            if (!readers.TryGetValue(name, out var list))
            {
                readers[name] = list = new List<string>();
                order.Add(name);
            }
            list.Add(by);
            if (guess != HudEventType.Invalid) guessed.TryAdd(name, guess);
        }

        foreach (var document in documents)
            foreach (var block in HudLayout.TopLevelElements(document)) Walk(block, Read);
        foreach (var node in transforms.Nodes)
            foreach (var input in node.Inputs)
                Read(input, $"{node.Kind} \"{node.InstanceName}\"", node switch
                {
                    NameMeshNode => HudEventType.Uint,
                    ColorBlendNode blend when string.Equals(input, blend.Blend, StringComparison.OrdinalIgnoreCase) => HudEventType.Color,
                    _ => HudEventType.Float,
                });

        var writers = new Dictionary<string, (HudEventType Type, List<string> By)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, type, from) in transforms.Outputs)
        {
            if (!writers.TryGetValue(name, out var w))
            {
                writers[name] = w = (type, new List<string>());
                if (!readers.ContainsKey(name)) order.Add(name);
            }
            w.By.Add($"{from.Kind} \"{from.InstanceName}\"");
        }

        var uses = new List<HudEventUse>();
        foreach (var name in order)
        {
            var known = HudEventCatalog.Find(name);
            var written = writers.TryGetValue(name, out var w) ? w : default;
            var type = known?.Type ?? (written.By is not null ? written.Type : guessed.TryGetValue(name, out var g) ? g : HudEventType.Bool);
            var source = known?.Source ?? (written.By is not null ? HudEventSource.Transform : HudEventSource.Unknown);
            uses.Add(new HudEventUse(known?.Name ?? name, type, source, known?.Note,
                readers.TryGetValue(name, out var r) ? r : (IReadOnlyList<string>)Array.Empty<string>(),
                written.By ?? (IReadOnlyList<string>)Array.Empty<string>()));
        }
        return uses;
    }

    private static void Walk(HudBlock block, Action<string, string, HudEventType> read)
    {
        if (!ElementKinds.TryGet(block, out var kind)) return;
        var spec = ElementReader.Read(block, kind);
        var who = $"{block.Name} \"{spec.Name}\"";
        foreach (var (property, name) in spec.Bindings)
            read(name, $"{who} {property}", property switch
            {
                "EventValue" or "EventNumber" or "EventAlpha" => HudEventType.Float,
                "EventText" => HudEventType.String,
                "EventColor" => HudEventType.Color,
                "EventBitmap" => HudEventType.Uint,
                "EventMesh" => HudEventType.Model,
                "EventPosition" or "EventScale" or "EventRotation" => HudEventType.Vector3,
                _ => HudEventType.Invalid,
            });
        foreach (var child in spec.Children) Walk(child, read);
    }
}
