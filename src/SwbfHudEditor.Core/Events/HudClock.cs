using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Events;

public enum FadePhase { Inactive, Attack, Hold, Sustain, Release }

public enum EnableState { Invalid, Enabling, Enabled, Disabling, Disabled }

/// <summary>
/// An element's fader, <c>HUD::Envelope</c> (P <c>0060F040</c> Update, <c>0060EE30</c> level, <c>0060EF40</c> Start,
/// <c>0060EFE0</c> Stop; the same on modtools). A phase change is decided on the time before the frame's dt is
/// added, the overshoot carried into the next phase; a phase of length 0 is skipped.
/// </summary>
public sealed class Envelope
{
    /// <summary>The phases' lengths: -, FadeInTime, FadeHoldTime (endless by default), FadeSustainTime, FadeOutTime.</summary>
    public readonly float[] Lengths = { 0f, 0f, float.MaxValue, 0f, 0f };
    public float Time;
    public FadePhase Phase = FadePhase.Inactive;

    public FadePhase Update(float dt)
    {
        var p = Phase;
        if (p > FadePhase.Inactive)
            while (true)
            {
                var length = Lengths[(int)p];
                if (length != 0f && Time <= length) break;
                p++;
                Phase = p;
                Time -= length;
                if (p > FadePhase.Release)
                {
                    Phase = p = FadePhase.Inactive;
                    break;
                }
            }
        Time += dt;
        return p;
    }

    public float Level => Phase switch
    {
        FadePhase.Attack => Time < Lengths[1] ? Time / Lengths[1] : 1f,
        FadePhase.Hold or FadePhase.Sustain => 1f,
        FadePhase.Release => Time < Lengths[4] ? 1f - Time / Lengths[4] : 0f,
        _ => 0f,
    };

    /// <summary>From a fade-out it keeps the share of the time elapsed, so the level jumps to 1 - L (a game bug).</summary>
    public void Start()
    {
        switch (Phase)
        {
            case FadePhase.Inactive: Time = 0f; Phase = FadePhase.Attack; break;
            case FadePhase.Hold: Time = 0f; break;
            case FadePhase.Sustain: Time = 0f; Phase = FadePhase.Hold; break;
            case FadePhase.Release:
                Time = Lengths[4] != 0f ? Time / Lengths[4] * Lengths[1] : 0f;
                Phase = FadePhase.Attack;
                break;
        }
    }

    /// <summary>From a fade-in it keeps the share of the time elapsed, so the level jumps to 1 - L; Sustain is skipped.</summary>
    public void Stop()
    {
        if (Phase == FadePhase.Attack)
        {
            Time = Lengths[1] != 0f ? Time / Lengths[1] * Lengths[4] : 0f;
            Phase = FadePhase.Release;
        }
        else if (Phase == FadePhase.Hold)
        {
            Time = 0f;
            Phase = FadePhase.Sustain;
        }
    }
}

/// <summary>
/// A BarBitmap's change strip as it stands: the part of the bar it covers, as shares of the bar's full width
/// (<paramref name="From"/> may be past <paramref name="To"/>), how far its top has come down (0 to 1, a shrink
/// only), and its alpha.
/// </summary>
public readonly record struct StripState(float From, float To, float Collapse, bool Grew, float Alpha);

/// <summary>One element on the clock: its fader, colour, pulse and, for a bar, its change strip.</summary>
public sealed class ElementClock
{
    private readonly HudClock _clock;
    internal readonly Envelope Fader = new();
    internal EnableState State = EnableState.Disabled;
    internal bool RenderEnabled;
    internal readonly byte[] Color = { 255, 255, 255, 255 };
    internal readonly byte[] ColorDefault = { 255, 255, 255, 255 };
    internal readonly byte[] ColorChange = { 255, 255, 255, 255 };
    internal float ChangeRate = 0.3f;
    internal float PulseRate = float.MaxValue;
    internal float PulseTime = float.MaxValue;
    internal bool UseChangeColor;
    internal float Alpha = 1f;
    internal string? FadeOutEvent;
    /// <summary>When it last came on, in the order of the clock's sends.</summary>
    public long EnableSeq { get; internal set; }

    // A BarBitmap: its value and change strip (ElementBarBitmap, P 005F7920).
    internal BarClock? Bar;

    internal ElementClock(HudClock clock) => _clock = clock;

    /// <summary>Drawn: its render element is enabled.</summary>
    public bool Shown => RenderEnabled;

    /// <summary>The fader's level, 0 to 1.</summary>
    public float Level => Fader.Level;

    public FadePhase Phase => Fader.Phase;

    /// <summary>Its colour as drawn this frame, the alpha byte its fader and <c>Alpha</c> (or an <c>EventAlpha</c>) wrote.</summary>
    public HudColor DrawColor => new(Color[0], Color[1], Color[2], Color[3]);

    /// <summary>A bar's change strip, while it shows.</summary>
    public StripState? Strip => Bar is { Strip: { RenderEnabled: true } strip } bar
        ? new StripState(bar.StripFrom, bar.StripTo, bar.Grew ? 0f : bar.Collapse, bar.Grew, strip.Color[3] / 255f)
        : null;

    /// <summary><c>Element::Update</c> (P <c>005F5870</c>).</summary>
    internal void Update(float dt)
    {
        var before = Fader.Phase;
        var after = Fader.Update(dt);
        if (after != before)
        {
            var s = after switch
            {
                FadePhase.Inactive => EnableState.Disabled,
                FadePhase.Attack => EnableState.Enabling,
                FadePhase.Hold => EnableState.Enabled,
                _ => EnableState.Disabling,
            };
            if (FadeOutEvent is not null && s is EnableState.Disabling or EnableState.Disabled)
                _clock.Dispatch(FadeOutEvent, HudEventValue.FromBool(true));
            UpdateEnableState(s);
        }
        if (!RenderEnabled) return;
        UpdateColour(dt);
        Color[3] = unchecked((byte)(int)((float)(Fader.Level * 255f) * Alpha));
    }

    /// <summary><c>Element::UpdateEnableState</c> (P <c>005F5C00</c>).</summary>
    internal void UpdateEnableState(EnableState s)
    {
        switch (s)
        {
            case EnableState.Enabling:
                if (State != EnableState.Enabling && State != EnableState.Enabled)
                {
                    RenderEnabled = true;
                    EnableSeq = _clock.NextSeq();
                }
                Fader.Start();
                break;
            case EnableState.Disabling:
                if (State is EnableState.Disabling or EnableState.Disabled) return;
                Fader.Stop();
                break;
            case EnableState.Disabled:
                if (RenderEnabled)
                {
                    Color[0] = ColorDefault[0];
                    Color[1] = ColorDefault[1];
                    Color[2] = ColorDefault[2];
                    RenderEnabled = false;
                }
                break;
        }
        State = s;
    }

    /// <summary>The handlers of <c>EventEnable</c> and <c>EventDisable</c>, and <c>Element::Enable</c>: the state, then 0.001 s.</summary>
    internal void Enable(bool on)
    {
        UpdateEnableState(on ? EnableState.Enabling : EnableState.Disabling);
        Update(0.001f);
        Bar?.AfterOwnUpdate();
    }

    /// <summary>The colour eases back to its default per byte, per frame; a pulse snaps it to ColorChange (P <c>005F5951</c>).</summary>
    private void UpdateColour(float dt)
    {
        float k;
        if (ChangeRate == 0f) k = 1f;
        else
        {
            k = 1f / ChangeRate * dt;
            k = k < 0f ? 0f : MathF.Min(1f, k);
        }
        for (var c = 0; c < 4; c++)
            Color[c] = unchecked((byte)(int)(Color[c] * (1f - k) + ColorDefault[c] * k + 0.5f));

        PulseTime -= dt;
        if (!(PulseTime > 0f))
        {
            Array.Copy(ColorChange, Color, 4);
            PulseTime = PulseRate;
        }
    }

    /// <summary><c>Element::Changed</c> (P <c>005F2F60</c>): with UseChangeColor, the colour snaps to ColorChange.</summary>
    internal void Changed()
    {
        if (!UseChangeColor) return;
        Color[0] = ColorChange[0];
        Color[1] = ColorChange[1];
        Color[2] = ColorChange[2];
    }

    internal void SetDefaultColor(HudColor c)
    {
        ColorDefault[0] = c.R;
        ColorDefault[1] = c.G;
        ColorDefault[2] = c.B;
        ColorDefault[3] = c.A;
    }

    /// <summary><c>Element::EventPulseRate</c> (P <c>005F3440</c>): a new rate restarts the countdown.</summary>
    internal void SetPulseRate(float rate)
    {
        if (MathF.Abs(rate - PulseRate) > 0.0001f) PulseTime = rate;
        PulseRate = rate;
    }
}

/// <summary>A BarBitmap's value and change strip (<c>ElementBarBitmap::SetValue</c>, P <c>005F7920</c>; Update, P <c>005F7C30</c>).</summary>
internal sealed class BarClock
{
    public float Min, Max = 1f, Value;
    public bool ScaleSize = true;
    public float FlashyIncFadeOutTime, FlashyDecFadeOutTime;
    public bool Grew, Armed;
    public float StripFrom, StripTo, Collapse;
    public required ElementClock Owner;
    public required ElementClock Strip;

    public void SetValue(float v)
    {
        var old = Value;
        Value = !(v >= Min) ? Min : v > Max ? Max : v;
        var frac = Max - Min == 0f ? 0f : (Value - Min) / (Max - Min);
        if (MathF.Abs(old - frac) > 0.0001f)        // the raw old value against the new share: a game quirk
        {
            if (ScaleSize)
            {
                var oldFrac = Max - Min == 0f ? 0f : (old - Min) / (Max - Min);
                if (Strip.RenderEnabled) Strip.Enable(false);
                Grew = frac > oldFrac;
                if (Armed)
                {
                    var fade = Grew ? FlashyIncFadeOutTime : FlashyDecFadeOutTime;
                    if (fade > 0.0001f)
                    {
                        // RefreshStrip (P 005F7650): the bar's Alpha, its fade time, white.
                        Strip.Alpha = Owner.Alpha;
                        Strip.Fader.Lengths[4] = fade;
                        StripFrom = frac;
                        StripTo = oldFrac;
                        Collapse = 0f;
                        Strip.Enable(true);
                    }
                }
            }
            Owner.Changed();
        }
        Armed = Owner.RenderEnabled;
    }

    /// <summary>After the bar's own update: a shrinking strip's top comes down over its first second.</summary>
    public void AfterOwnUpdate()
    {
        if (Strip.RenderEnabled && !Grew)
            Collapse = Strip.Fader.Phase == FadePhase.Release ? Math.Clamp(Strip.Fader.Time, 0f, 1f) : 0f;
    }
}

/// <summary>
/// The HUD over time: every send replayed at its time, a frame at a time in the game's order (each element's
/// update, then the frame's sends with their transforms, then GameExt's lerps; research for VisualHUD, timing).
/// Elements fade by their fader, ease and pulse their colour, and bars draw their change strips; GameExt's
/// Lerp eases and Compare holds by its hysteresis. It steps on from where it is while the clock runs forward.
/// </summary>
public sealed class HudClock
{
    /// <summary>One frame at 60 frames a second.</summary>
    public const float FrameTime = 1f / 60f;

    private readonly List<ElementClock> _elements = new();
    private readonly Dictionary<HudBlock, ElementClock> _byBlock = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, List<Action<HudEventValue>>> _handlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EventSend> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LerpState> _lerps = new();
    private readonly List<EventSend> _sends;
    private readonly HudTransformGraph _transforms;
    private readonly Func<uint, string?>? _localize;
    private int _nextSend;
    private long _seq;
    private int _depth;

    /// <summary>The frame stepped to, -1 before the first.</summary>
    public long Frame { get; private set; } = -1;

    /// <summary>That frame's time.</summary>
    public double Time => Frame * (double)FrameTime;

    /// <summary>The latest value of every event, as of this frame, transforms' outputs included.</summary>
    public IReadOnlyDictionary<string, EventSend> Values => _values;

    public ElementClock? For(HudBlock block) => _byBlock.TryGetValue(block, out var e) ? e : null;

    /// <summary>Every element starts shown, for the editor's view where the events do not decide what shows.</summary>
    public bool AllShown { get; }

    private HudClock(IReadOnlyList<HudDocument> documents, HudTransformGraph transforms, IReadOnlyList<EventSend> history,
        Func<uint, string?>? localize, bool allShown)
    {
        _transforms = transforms;
        _localize = localize;
        AllShown = allShown;
        _sends = history.OrderBy(s => s.Time).ThenBy(s => s.Seq).ToList();
        // Element::sList: construction order, the order the files are read.
        foreach (var document in documents)
            foreach (var block in HudLayout.TopLevelElements(document))
                Add(block);
        foreach (var node in transforms.Nodes) Register(node);
        if (!allShown) return;
        // As if each had been enabled before the first frame and finished fading in: colours ease and pulse,
        // and bars draw their change strips.
        foreach (var e in _elements)
        {
            e.RenderEnabled = true;
            e.State = EnableState.Enabled;
            e.Fader.Phase = FadePhase.Hold;
            e.Fader.Time = 0f;
            e.Color[3] = unchecked((byte)(int)(255f * e.Alpha));
            if (e.Bar is { } bar) bar.Armed = true;
        }
    }

    /// <summary>A clock at its first frame for these files, transforms and sends; step it on with <see cref="RunTo"/>.</summary>
    public static HudClock Start(IReadOnlyList<HudDocument> documents, HudTransformGraph transforms, HudEventState state,
        Func<uint, string?>? localize = null, bool allShown = false) => new(documents, transforms, state.History, localize, allShown);

    /// <summary>The frame that holds <paramref name="time"/>: the last one <see cref="RunTo"/> steps.</summary>
    public static long FrameFor(double time) => Math.Max(0, (long)Math.Ceiling(time / FrameTime - 1e-6));

    internal long NextSeq() => ++_seq;

    /// <summary>Step frames until the frame that holds <paramref name="time"/>, so a send made then has been run.</summary>
    public void RunTo(double time)
    {
        while (Frame * (double)FrameTime < time - 1e-9 || Frame < 0) Step();
    }

    private void Step()
    {
        Frame++;
        var dt = Frame == 0 ? 0f : FrameTime;
        var now = Frame * (double)FrameTime;

        // 1. Every element's update, in construction order (a bar, then its strip).
        foreach (var e in _elements)
        {
            if (e.Fader.Phase == FadePhase.Inactive && !e.RenderEnabled) continue;
            e.Update(dt);
            if (e.Bar is { } bar)
            {
                bar.Strip.Update(dt);
                bar.AfterOwnUpdate();
            }
        }

        // 2. The sends of this frame, in order, each dispatched at once.
        while (_nextSend < _sends.Count && _sends[_nextSend].Time <= now + 1e-9)
        {
            var send = _sends[_nextSend++];
            Dispatch(send.Name, send.Value);
        }

        // 3. GameExt's lerps tick, the newest declared first, and publish what changed.
        for (var i = _lerps.Count - 1; i >= 0; i--)
        {
            var lerp = _lerps[i];
            lerp.Advance(dt);
            lerp.Publish(again: lerp.Node.OutputIsAlpha);
        }
    }

    /// <summary><c>Event::Send</c>: the value is the event's latest, and every handler runs at once.</summary>
    internal void Dispatch(string name, HudEventValue value, TransformNode? from = null)
    {
        if (_depth > 64) return;   // a loop of transforms
        _depth++;
        _values[name] = new EventSend(name, value, NextSeq(), from, Time);
        if (_handlers.TryGetValue(name, out var handlers))
            foreach (var handler in handlers.ToArray()) handler(value);
        _depth--;
    }

    private void On(string? name, Action<HudEventValue> handler)
    {
        if (name is null) return;
        if (!_handlers.TryGetValue(name, out var list)) _handlers[name] = list = new List<Action<HudEventValue>>();
        list.Add(handler);
    }

    private HudEventValue? Latest(string name) => _values.TryGetValue(name, out var s) ? s.Value : null;

    // ---- Elements ------------------------------------------------------------------------------------

    private void Add(HudBlock block)
    {
        if (!ElementKinds.TryGet(block, out var kind)) return;
        var spec = ElementReader.Read(block, kind);
        var e = new ElementClock(this) { Alpha = spec.Alpha };
        var (r, g, b) = spec.Color;
        e.SetDefaultColor(new HudColor(r, g, b));
        Array.Copy(e.ColorDefault, e.Color, 4);
        foreach (var line in block.Children)
        {
            if (line.HasBody || line.Arguments.Count == 0) continue;
            var a = line.Arguments;
            float F(int i) => i < a.Count ? EngineArgs.Float(a[i]) : 0f;
            switch (line.Name)
            {
                case "FadeInTime": e.Fader.Lengths[1] = F(0); break;
                case "FadeHoldTime": e.Fader.Lengths[2] = F(0); break;
                case "FadeSustainTime": e.Fader.Lengths[3] = F(0); break;
                case "FadeOutTime": e.Fader.Lengths[4] = F(0); break;
                case "ColorChange" when a.Count >= 3:
                    e.ColorChange[0] = (byte)Math.Clamp((int)F(0), 0, 255);
                    e.ColorChange[1] = (byte)Math.Clamp((int)F(1), 0, 255);
                    e.ColorChange[2] = (byte)Math.Clamp((int)F(2), 0, 255);
                    e.ColorChange[3] = 255;
                    break;
                case "ColorChangeRate": e.ChangeRate = F(0); break;
                case "ColorPulseRate": e.PulseRate = e.PulseTime = F(0); break;
                case "UseChangeColor": e.UseChangeColor = F(0) != 0f; break;
                case "EventFadeOut": e.FadeOutEvent = EngineArgs.String(a[0]); break;
            }
        }
        _elements.Add(e);
        _byBlock[block] = e;

        On(spec.EventEnable, _ => e.Enable(true));
        On(spec.EventDisable, _ =>
        {
            if (e.Bar is { } bar) bar.Armed = false;
            e.Enable(false);
        });
        On(spec.EventColor, v => { if (v.Type == HudEventType.Color) e.SetDefaultColor(v.Color); });
        On(Name(block, "EventChanged"), _ => e.Changed());
        On(Name(block, "EventPulseRate"), v => { if (v.IsNumber) e.SetPulseRate(v.AsFloat()); });
        if (ElementKinds.IsGroupBased(kind))
            On(spec.EventAlpha, v => { if (v.IsNumber) e.Color[3] = unchecked((byte)(int)(v.AsFloat() * 255f)); });
        if (kind == ElementKind.Text)
        {
            On(spec.EventText, _ => e.Changed());
            On(spec.EventNumber, _ => e.Changed());
        }
        if (ElementKinds.IsBitmapBased(kind)) On(spec.EventBitmap, _ => e.Changed());

        if (kind == ElementKind.BarBitmap)
        {
            // The strip is an element of its own, made in the bar's constructor: A, H and S are 0.
            var strip = new ElementClock(this);
            strip.Fader.Lengths[2] = 0f;
            var bar = new BarClock
            {
                Owner = e,
                Strip = strip,
                Min = spec.MinValue,
                Max = spec.MaxValue,
                Value = spec.Value,
                ScaleSize = spec.ScaleSize,
                FlashyIncFadeOutTime = Number(block, "FlashyIncFadeOutTime"),
                FlashyDecFadeOutTime = Number(block, "FlashyDecFadeOutTime"),
            };
            e.Bar = bar;
            On(spec.EventValue, v => { if (v.IsNumber) bar.SetValue(v.AsFloat()); });
        }
        else if (kind == ElementKind.BarSegmented)
            On(spec.EventValue, _ => e.Changed());

        foreach (var child in spec.Children) Add(child);
    }

    private static string? Name(HudBlock block, string key) =>
        block.Children.LastOrDefault(c => !c.HasBody && c.Name == key && c.Arguments.Count > 0) is { } line
            ? EngineArgs.String(line.Arguments[0])
            : null;

    private static float Number(HudBlock block, string key) =>
        block.Children.LastOrDefault(c => !c.HasBody && c.Name == key && c.Arguments.Count > 0) is { } line
            ? EngineArgs.Float(line.Arguments[0])
            : 0f;

    // ---- Transforms ------------------------------------------------------------------------------------

    private void Register(TransformNode node)
    {
        if (node.Problem is not null) return;
        switch (node)
        {
            case LerpNode lerp:
            {
                var state = new LerpState(this, lerp);
                _lerps.Add(state);
                foreach (var input in lerp.Inputs.Distinct(StringComparer.OrdinalIgnoreCase))
                    On(input, _ => state.Inputs());
                break;
            }
            case OperandNode { Mode: OperandKind.Compare } compare:
            {
                var state = new CompareState(this, compare);
                foreach (var input in compare.Inputs.Distinct(StringComparer.OrdinalIgnoreCase))
                    On(input, _ => state.Inputs());
                break;
            }
            case OperandNode math:
            {
                float? last = null;
                foreach (var input in math.Inputs.Distinct(StringComparer.OrdinalIgnoreCase))
                    On(input, _ =>
                    {
                        // Published at once, only when the result changed.
                        foreach (var (name, value) in math.Run(Latest).ToList())
                        {
                            if (last == value.Float && !math.OutputIsAlpha) continue;
                            last = value.Float;
                            Dispatch(name, value, math);
                        }
                    });
                break;
            }
            default:
                foreach (var trigger in node.Triggers.Distinct(StringComparer.OrdinalIgnoreCase))
                    On(trigger, _ =>
                    {
                        foreach (var (name, value) in node.Run(Latest).ToList()) Dispatch(name, value, node);
                    });
                break;
        }
    }

    /// <summary>GameExt's TransformNumberLerp (BF2GameExt <c>hud_number_math_core.hpp</c>, 142 to 226).</summary>
    private sealed class LerpState(HudClock clock, LerpNode node)
    {
        public LerpNode Node { get; } = node;
        private double _target;
        private double _s;
        private bool _targetKnown;
        private bool _started;
        private bool _sent;
        private float _previous;

        public void Inputs()
        {
            Retarget();
            Publish(again: false);
        }

        private void Retarget()
        {
            _targetKnown = false;
            if (Node.Input is null || clock.Latest(Node.Input) is not { IsNumber: true } input) return;
            if (Node.RangeMin.Value(clock.Latest) is not { } lo || Node.RangeMax.Value(clock.Latest) is not { } hi || lo == hi) return;
            var t = (Operand.ValueOf(input) - lo) / (hi - lo);
            if (!double.IsFinite(t)) return;
            _target = Math.Clamp(t, 0, 1);
            _targetKnown = true;
            if (!_started)
            {
                _s = _target;
                _started = true;
                return;
            }
            if (_target > _s && !(Node.RiseTime > 0)) _s = _target;
            if (_target < _s && !(Node.FallTime > 0)) _s = _target;
        }

        public void Advance(double dt)
        {
            if (!_started || !_targetKnown || !(dt > 0) || _s == _target) return;
            _s = _s < _target
                ? Node.RiseTime > 0 ? Math.Min(_target, _s + dt / Node.RiseTime) : _target
                : Node.FallTime > 0 ? Math.Max(_target, _s - dt / Node.FallTime) : _target;
        }

        public void Publish(bool again)
        {
            if (!_started || Node.Output is null) return;
            if (Node.A.Value(clock.Latest) is not { } a || Node.B.Value(clock.Latest) is not { } b) return;
            var r = a + (b - a) * _s;
            if (!double.IsFinite(r) || Math.Abs(r) > float.MaxValue) return;
            var output = (float)r;
            if (_sent && _previous == output && !again) return;
            _previous = output;
            _sent = true;
            clock.Dispatch(Node.Output, HudEventValue.FromFloat(output), Node);
        }
    }

    /// <summary>GameExt's TransformNumberCompare with its hysteresis (<c>hud_number_math_core.hpp</c>, 228 to 272).</summary>
    private sealed class CompareState(HudClock clock, OperandNode node)
    {
        private bool? _on;

        public void Inputs()
        {
            if (node.A.Value(clock.Latest) is not { } a || node.B.Value(clock.Latest) is not { } b) return;
            var on = _on ?? false;
            var h = (double)node.Hysteresis;
            var result = node.Operation switch
            {
                "Greater" => on ? a > b - h : a > b,
                "GreaterOrEqual" => on ? a >= b - h : a >= b,
                "Less" => on ? a < b + h : a < b,
                "LessOrEqual" => on ? a <= b + h : a <= b,
                "Equal" => Math.Abs(a - b) <= h,
                _ => Math.Abs(a - b) > h,
            };
            if (_on == result && !node.OutputIsAlpha) return;
            var changed = _on != result;
            _on = result;
            if (node.Output is not null) clock.Dispatch(node.Output, HudEventValue.FromFloat(result ? 1f : 0f), node);
            if (!changed) return;
            if (result && node.OutputTrue is not null) clock.Dispatch(node.OutputTrue, HudEventValue.FromBool(true), node);
            if (!result && node.OutputFalse is not null) clock.Dispatch(node.OutputFalse, HudEventValue.FromBool(true), node);
        }
    }
}
