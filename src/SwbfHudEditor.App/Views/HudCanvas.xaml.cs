using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SwbfHudEditor.Core.Assets;
using SwbfHudEditor.Core.Edits;
using SwbfHudEditor.Core.Events;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;
using SwbfHudEditor.Core.Localization;
using WpfMatrix = System.Windows.Media.Matrix;

namespace SwbfHudEditor.App.Views;

/// <summary>
/// Draws a <c>.hud</c> document as the game does on a chosen screen: the layout and every matrix come
/// from <see cref="HudLayout"/> (SwbfHudEditor.Core/Layout), and this control only turns each element's
/// quad, text or model into a WPF visual under the matrix the game draws it with. The canvas is the
/// screen, one unit a screen pixel.
/// </summary>
public partial class HudCanvas : UserControl
{
    private static readonly (int Width, int Height, string Shape)[] Screens =
    {
        (1024, 768, "4:3"), (1280, 960, "4:3"), (1600, 1200, "4:3"), (1280, 1024, "5:4"),
        (1280, 720, "16:9"), (1366, 768, "16:9"), (1600, 900, "16:9"), (1920, 1080, "16:9"),
        (2560, 1440, "16:9"), (3840, 2160, "16:9"),
        (1280, 800, "16:10"), (1680, 1050, "16:10"), (1920, 1200, "16:10"),
        (2560, 1080, "21:9"), (3440, 1440, "21:9"), (5120, 1440, "32:9"),
    };

    // The files of the HUD in load order, and the one the guides and the layout line describe.
    private IReadOnlyList<HudDocument> _documents = Array.Empty<HudDocument>();
    private HudDocument? _activeDocument;
    private TextureCache? _textures;
    private TextFonts _fonts = new(null);
    private TextPreview _textPreview = TextPreview.Examples;
    private BitmapSource? _reference;
    private MeshRenderer? _meshRenderer;
    private LocalizationTable? _localization;
    private HudTransformGraph? _graph;
    private HudEventState? _events;

    private HudTarget _target = HudTarget.GameExt;
    private HudScreen _screen = new(1280, 720);
    private int _activeViewport = 1;
    private HudLayoutResult? _layout;
    private readonly Dictionary<HudBlock, FrameworkElement> _visuals = new(ReferenceEqualityComparer.Instance);
    private readonly List<FrameworkElement> _highlight = new();
    private bool _ready;
    private bool _fitOnNextLayout = true;

    // Drag state: the element pressed, everything that moves with it, and their Position numbers before.
    private HudBlock? _dragBlock;
    private Point _dragStartMouse;
    private List<HudBlock> _dragBlocks = new();
    private List<ArgumentChange> _dragOriginals = new();
    private bool _isDragging;
    private const double DragThresholdPx = 3;
    // A press on an element of a selection of several keeps the selection, to drag it all; released
    // without a drag, that element alone is picked.
    private HudBlock? _pickOnRelease;

    // Snapping while dragging: what is dragged, on the screen, before it moved, and the lines it can meet.
    private Rect _dragStartBounds = Rect.Empty;
    private readonly List<double> _snapXs = new();
    private readonly List<double> _snapYs = new();
    private readonly List<FrameworkElement> _guides = new();
    private const double SnapPx = 6;
    private static readonly Brush GuideBrush = GuideColour();
    // Alt held during a drag: its release must not open the menu.
    private bool _altInDrag;

    // Click-cycle selection: clicking again at the same spot steps down through a stack of elements.
    private HudBlock? _currentSelection;
    private Point _lastDownPos = new(-9999, -9999);
    private bool _clickCycleArmed;
    private const double CycleTolerancePx = 3.0;

    public event EventHandler<HudBlock>? BlockSelected;
    public event EventHandler<string?>? HoverInfoChanged;
    /// <summary>Fires while a block is being dragged. Listeners typically refresh the property panel.</summary>
    public event EventHandler<HudBlock>? BlockDragged;
    /// <summary>Fires once when a drag ends with a real position change, carrying the undoable edit.</summary>
    public event EventHandler<IHudEdit>? BlockDragCommitted;
    /// <summary>Fires after each render with the events' effect, or null when no events apply.</summary>
    public event EventHandler<HudSimulation?>? SimulationUpdated;

    public HudCanvas()
    {
        InitializeComponent();
        foreach (var (w, h, shape) in Screens)
            ScreenCombo.Items.Add(new ComboBoxItem { Content = $"{w}×{h}  {shape}", Tag = new HudScreen(w, h) });
        ScreenCombo.SelectedIndex = Array.FindIndex(Screens, s => s.Width == 1280 && s.Height == 720);
        RoundingBox.IsChecked = DefaultRounding(_target);
        _ready = true;
        ApplyScreen();
        Loaded += (_, _) => FitIfPending();
    }

    /// <summary>
    /// Show the files of a HUD, in load order. The game loads them additively: their elements share the
    /// screen groups and their transforms and events are global, while each file is laid out by its own
    /// <c>FileInfo</c>.
    /// </summary>
    public void LoadDocuments(IReadOnlyList<HudDocument> docs, AssetResolver? assets, LocalizationTable? localization = null)
    {
        _documents = docs;
        _clockStale = true;
        if (_activeDocument is not null && !docs.Contains(_activeDocument)) _activeDocument = null;
        _textures = assets is not null ? new TextureCache(assets) : null;
        _fonts = new TextFonts(assets);
        _meshRenderer = _textures is not null && assets is not null ? new MeshRenderer(assets, _textures) : null;
        _localization = localization;
        _tinted.Clear();
        Render();
    }

    /// <summary>The file the guides and the layout line describe, such as the selection's.</summary>
    public HudDocument? ActiveDocument
    {
        get => _activeDocument;
        set
        {
            if (ReferenceEquals(_activeDocument, value)) return;
            _activeDocument = value;
            Render();
        }
    }

    /// <summary>What text elements show before their events: example values, long ones, or the keys.</summary>
    public TextPreview TextPreview
    {
        get => _textPreview;
        set
        {
            if (_textPreview == value) return;
            // The box's handler takes it from here.
            TextPreviewCombo.SelectedIndex = (int)value;
        }
    }

    /// <summary>
    /// An image to lay the HUD over, such as a screenshot of the game or a mock-up, stretched across
    /// the screen; null for none. Editor-only: it is never saved into a file.
    /// </summary>
    public BitmapSource? ReferenceImage
    {
        get => _reference;
        set
        {
            _reference = value;
            _ready = false;
            ReferenceBox.IsEnabled = value is not null;
            ReferenceBox.IsChecked = value is not null;
            _ready = true;
            Render();
        }
    }

    /// <summary>The reference image's opacity, 0.1 to 1.</summary>
    public double ReferenceOpacity
    {
        get => ReferenceOpacitySlider.Value;
        set => ReferenceOpacitySlider.Value = Math.Clamp(value, ReferenceOpacitySlider.Minimum, ReferenceOpacitySlider.Maximum);
    }

    /// <summary>Whether the reference image is drawn over the HUD rather than under it.</summary>
    public bool ReferenceOnTop
    {
        get => ReferenceOnTopBox.IsChecked == true;
        set => ReferenceOnTopBox.IsChecked = value;
    }

    /// <summary>Whether the reference image is shown at all.</summary>
    public bool ReferenceShown
    {
        get => ReferenceBox.IsChecked == true;
        set => ReferenceBox.IsChecked = value && _reference is not null;
    }

    /// <summary>Fires when the reference image's opacity or place changes, for the host to remember.</summary>
    public event EventHandler? ReferenceSettingsChanged;

    /// <summary>Editor-only: whether a file's elements are drawn. Null draws every file.</summary>
    public Func<HudDocument, bool>? DocumentVisible { get; set; }

    /// <summary>The transforms of every file, in load order, as last drawn.</summary>
    public HudTransformGraph? Transforms => _graph;

    /// <summary>How a file is laid out with the toolbar's options, for a status line.</summary>
    public string? DescribeFile(HudDocument doc) => _layout?.LayoutOf(doc) is { } file ? Describe(_layout.Options, file) : null;

    /// <summary>
    /// The events sent, which set values, texts, colours, models and moves; with null, every element is
    /// in its load state. What shows follows them only with <see cref="ShowByEvents"/>.
    /// </summary>
    public void SetEventState(HudEventState? events)
    {
        if (_events is not null)
        {
            _events.Changed -= Events_Changed;
            _events.ClockChanged -= Events_ClockChanged;
        }
        _events = events;
        _clock = null;
        if (_events is not null)
        {
            _events.Changed += Events_Changed;
            _events.ClockChanged += Events_ClockChanged;
        }
        Render();
    }

    // ---- The clock: the sends replayed at their times, stepped on as it runs --------------------------

    private HudClock? _clock;
    private long _clockVersion = -1;
    private bool _clockStale = true;
    private bool _renderPending;

    /// <summary>
    /// The clock at the event state's time: started again when the sends, the files or the mode changed, or
    /// time went back. At 0 s there is none: every send shows what it ends with, fades done, lerps there,
    /// until the clock is run or dragged.
    /// </summary>
    private HudClock? ClockNow(Func<uint, string?>? localize)
    {
        if (_events is null || _graph is null || _events.Now <= 0) return null;
        var allShown = !_showByEvents;
        if (_clock is null || _clockStale || _clock.AllShown != allShown || _clockVersion != _events.Version ||
            _clock.Frame > HudClock.FrameFor(_events.Now))
        {
            _clock = HudClock.Start(_documents, _graph, _events, localize, allShown);
            _clockVersion = _events.Version;
            _clockStale = false;
        }
        _clock.RunTo(_events.Now);
        return _clock;
    }

    /// <summary>One render for any number of clock ticks queued before it runs.</summary>
    private void Events_ClockChanged(object? sender, EventArgs e)
    {
        if (_renderPending) return;
        _renderPending = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            _renderPending = false;
            _reuseVisuals = true;
            Render();
        }));
    }

    private bool _showByEvents;
    /// <summary>
    /// The events decide what shows, as in the game: an element shows once its EventEnable has come
    /// after its EventDisable, and only while every group above it shows. Off, every element shows but
    /// those hidden in the tree, and the events set only what they set.
    /// </summary>
    public bool ShowByEvents
    {
        get => _showByEvents;
        set
        {
            if (_showByEvents == value) return;
            _showByEvents = value;
            _clockStale = true;
            Render();
        }
    }

    private void Events_Changed(object? sender, EventArgs e)
    {
        _reuseVisuals = true;
        Render();
    }

    /// <summary>
    /// Re-render without re-parsing or re-resolving assets. Call after an edit or a visibility change:
    /// the elements the edit did not change keep their visuals.
    /// </summary>
    public void Refresh()
    {
        _clockStale = true;
        _reuseVisuals = true;
        Render();
    }

    private static bool DefaultRounding(HudTarget target) => target == HudTarget.Vanilla;

    /// <summary>Set every toolbar option at once (the snapshot mode drives the canvas this way).</summary>
    public void Configure(HudTarget target, HudScreen screen, bool? rounding, int viewport, bool placeholders, bool guides)
    {
        _ready = false;
        TargetCombo.SelectedIndex = target == HudTarget.Vanilla ? 1 : 0;
        _target = target;
        RoundingBox.IsChecked = rounding ?? DefaultRounding(target);
        ViewportCombo.SelectedIndex = Math.Clamp(viewport, 1, 4) - 1;
        _activeViewport = Math.Clamp(viewport, 1, 4);
        ShowPlaceholdersBox.IsChecked = placeholders;
        GuidesBox.IsChecked = guides;
        var index = Array.FindIndex(Screens, s => s.Width == screen.Width && s.Height == screen.Height);
        if (index < 0)
        {
            ScreenCombo.Items.Add(new ComboBoxItem { Content = $"{screen.Width}×{screen.Height}", Tag = screen });
            index = ScreenCombo.Items.Count - 1;
        }
        ScreenCombo.SelectedIndex = index;
        _screen = screen;
        _ready = true;
        ApplyScreen();
    }

    /// <summary>Every element as last laid out, one line each, for checking a render by text.</summary>
    public string DescribeLayout()
    {
        if (_layout is null) return "(no document)";
        var text = new System.Text.StringBuilder();
        text.AppendLine($"{_screen} {_target}");
        HudDocument? file = null;
        foreach (var e in _layout.All)
        {
            if (!ReferenceEquals(e.Document, file))
            {
                file = e.Document;
                text.AppendLine($"-- {FileName(file)}: {Describe(_layout.Options, e.FileLayout)}");
            }
            var at = ToScreen(e.ToScreen, 0, 0);
            var state = !e.InActiveViewport ? "other viewport" : e.Shown ? "shown" : e.Enabled ? "parent hidden" : "hidden";
            var what = new List<string>();
            if (e.Quad is { } q) what.Add($"quad {q.Width:0}x{q.Height:0}" + (e.Texture is { } t ? $" {t}" : " (no texture)"));
            if (e.Kind == ElementKind.Model3D)
                what.Add($"mesh {e.Mesh ?? "(none)"}{(e.Mesh is { } m && _meshRenderer?.Geometry(m) is null ? " (not found)" : "")} depth {e.MeshDepth:0.####}");
            if (e.Kind == ElementKind.Text) what.Add($"text \"{ShownText(e)}\"");
            if (ElementKinds.IsBar(e.Kind)) what.Add($"bar {e.BarFraction:0.##}");
            if (e.Role != PieceRole.None) what.Add($"{e.Role} slid {e.Slide:0.#}");
            what.Add(_visuals.ContainsKey(e.Block) ? "drawn" : "not drawn");
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{new string(' ', e.Depth * 2)}{e.Kind} \"{e.Spec.Name}\" {state} at ({at.X:0.#}, {at.Y:0.#}) {string.Join(", ", what)}"));
        }
        return text.ToString();
    }

    /// <summary>The canvas as an image the size of the screen, over <paramref name="background"/> if given.</summary>
    public BitmapSource RenderSnapshot(BitmapSource? background)
    {
        var w = _screen.Width;
        var h = _screen.Height;
        var room = new Size(w + 400, h + 400);
        Measure(room);
        Arrange(new Rect(room));
        UpdateLayout();

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var area = new Rect(0, 0, w, h);
            if (background is not null) dc.DrawImage(background, area);
            else dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2a, 0x2e, 0x33)), null, area);
            dc.DrawRectangle(new VisualBrush(CanvasSurface)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = area,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = area,
            }, null, area);
        }
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    // ---- Rendering ------------------------------------------------------------------------------

    // ---- Reusing visuals: while only values change (the clock running, events sent), an element whose
    // drawing is the same keeps its visual, and only the others are made again -------------------------

    /// <summary>An element, or the <c>Piece</c>th piece its code built.</summary>
    private readonly record struct VisualId(HudBlock Block, int Piece);

    /// <summary>Everything that can change how an element is drawn: its values, and its own lines as written.</summary>
    private readonly record struct VisualSignature(ElementKind Kind, string? Texture, QuadRect? Quad, TexRect Uv, Vector3 Color,
        float Alpha, Matrix4x4 ToScreen, Matrix4x4 World, string? Mesh, float Depth, string? Mask, TexRect MaskUv, string? Text,
        int BoxWidth, int BoxHeight, bool Shown, bool Simulated, int Segments, int Lines, bool Additive);

    /// <summary>
    /// A block's own lines and the blocks inside it that are not elements (templates, MeshInfo, Format),
    /// as written: an edit to any of them changes how the element is drawn. Elements inside it have their own.
    /// </summary>
    private static int LinesHash(HudBlock block)
    {
        var hash = new HashCode();
        void Add(HudBlock b)
        {
            hash.Add(b.Name);
            foreach (var a in b.Arguments) hash.Add(a.Raw);
            foreach (var c in b.Children)
                if (!ElementKinds.TryGet(c, out _))
                    Add(c);
        }
        Add(block);
        return hash.ToHashCode();
    }

    private readonly Dictionary<VisualId, (VisualSignature Signature, FrameworkElement? Visual)> _visualCache = new();
    private bool _reuseVisuals;

    private static VisualId IdOf(LaidOutElement e) =>
        e.Piece is { } p ? new VisualId(p.Owner.Block, p.Owner.Children.IndexOf(e)) : new VisualId(e.Block, -1);

    private VisualSignature SignatureOf(LaidOutElement e)
    {
        var segments = 0;
        if (e.Segments.Count > 0)
        {
            var hash = new HashCode();
            foreach (var segment in e.Segments)
            {
                hash.Add(segment.Lit);
                hash.Add(segment.ToScreen);
            }
            segments = hash.ToHashCode();
        }
        return new VisualSignature(e.Kind, e.Texture, e.Quad, e.Uv, e.DrawColor, e.DrawAlpha, e.ToScreen,
            e.Kind == ElementKind.Model3D ? e.World : default, e.Mesh, e.MeshDepth, e.Mask, e.MaskUv,
            e.Kind == ElementKind.Text ? e.Text ?? ShownText(e) : null, e.TextBoxWidth, e.TextBoxHeight, e.Shown,
            e.Runtime is not null, segments, LinesHash(e.Piece?.Owner.Block ?? e.Block), e.Additive);
    }

    /// <summary>How the canvas lays a block out now, if it is an element.</summary>
    public LaidOutElement? LaidOut(HudBlock block) => _layout?.Find(block);

    /// <summary>A texture's size in texels, if it is found.</summary>
    public (int Width, int Height)? TextureSizeOf(string name) =>
        _textures?.Get(name) is { } t ? (t.PixelWidth, t.PixelHeight) : null;

    /// <summary>The options the canvas last laid out with: its fonts and textures, its target.</summary>
    internal LayoutOptions? LayoutOptions => _layout?.Options;

    /// <summary>The resolution the canvas draws at.</summary>
    public HudScreen ScreenSize => _screen;

    /// <summary>The visual an element is drawn with, for tests.</summary>
    internal FrameworkElement? VisualOf(HudBlock block) => _visuals.TryGetValue(block, out var v) ? v : null;

    /// <summary>An element's visual, made and put on the canvas.</summary>
    private FrameworkElement? MakeVisual(LaidOutElement e)
    {
        var visual = CreateVisual(e);
        if (visual is null) return null;
        if (!e.Shown) visual.Opacity *= 0.2;
        visual.Tag = e.PickBlock;
        visual.MouseEnter += Visual_MouseEnter;
        visual.MouseLeave += Visual_MouseLeave;
        visual.Cursor = Cursors.SizeAll;
        ToolTipService.SetToolTip(visual, e.Piece is { } piece
            ? $"{piece.Owner.Block}: {(piece.Template is { } t ? t.Name : e.Kind.ToString())}, drawn by the game's code"
            : e.Block.ToString());
        Panel.SetZIndex(visual, e.DrawIndex);
        CanvasSurface.Children.Add(visual);
        _visuals[e.Block] = visual;
        return visual;
    }

    private void Render()
    {
        if (!_ready) return;
        var reuse = _reuseVisuals;
        _reuseVisuals = false;
        if (reuse)
        {
            // The elements' visuals stay; the guides, the reference and the highlights are drawn again.
            for (var i = CanvasSurface.Children.Count - 1; i >= 0; i--)
                if (CanvasSurface.Children[i] is not FrameworkElement { Tag: HudBlock })
                    CanvasSurface.Children.RemoveAt(i);
        }
        else
        {
            CanvasSurface.Children.Clear();
            _visualCache.Clear();
        }
        _visuals.Clear();
        _highlight.Clear();
        _layout = null;
        if (_documents.Count == 0)
        {
            CanvasSurface.Children.Clear();
            _visualCache.Clear();
            LayoutInfoText.Text = string.Empty;
            LayoutInfoText.ToolTip = null;
            return;
        }

        _graph = HudTransformGraph.From(_documents);
        HudSimulation? simulation = null;
        if (_events is not null && (_showByEvents || _events.Sends.Count > 0))
        {
            Func<uint, string?>? localize = _localization is { } table ? table.TryGetByHash : null;
            simulation = HudSimulation.Run(_documents, _graph, _events, localize, ClockNow(localize));
        }
        SimulationUpdated?.Invoke(this, simulation);

        _layout = HudLayout.Lay(_documents, new LayoutOptions
        {
            Screen = _screen,
            Target = _target,
            Rounding = RoundingBox.IsChecked == true,
            HudScale = (float)HudScaleSlider.Value,
            ActiveViewport = _activeViewport,
            ModelMesh = ChooseMesh,
            Simulation = simulation,
            ShowByEvents = _showByEvents,
            Fonts = name => _fonts.For(name).Metrics,
            TextureSize = name => _textures?.Get(name) is { } t ? (t.PixelWidth, t.PixelHeight) : null,
        });
        // The layout line and the guides are for one file: the active one, else the first with elements.
        var withElements = _layout.Files.Where(f => HudLayout.TopLevelElements(f.Document).Any()).ToList();
        var described = withElements.FirstOrDefault(f => ReferenceEquals(f.Document, _activeDocument));
        if (described.Document is null) described = withElements.Count > 0 ? withElements[0] : _layout.Files[0];
        var line = Describe(_layout.Options, described.Layout);
        LayoutInfoText.Text = withElements.Count > 1 ? $"{FileName(described.Document)}: {line}" : line;
        DrawReference();
        LayoutInfoText.ToolTip = withElements.Count > 1
            ? string.Join("\n", withElements.Select(f => $"{FileName(f.Document)}: {Describe(_layout.Options, f.Layout)}"))
            : null;
        if (GuidesBox.IsChecked == true) DrawGuides(_layout.Options.Screen, described.Layout);

        var showHidden = ShowHiddenBox.IsChecked == true;
        var seen = new HashSet<VisualId>();
        foreach (var e in _layout.All.OrderBy(e => e.DrawIndex))
        {
            if (!IsShown(e)) continue;
            // An element its events have not enabled is not drawn, or drawn faintly to keep it in reach.
            if (!e.Shown && !showHidden) continue;
            var id = IdOf(e);
            var signature = SignatureOf(e);
            seen.Add(id);
            if (_visualCache.TryGetValue(id, out var cached))
            {
                if (cached.Signature == signature)
                {
                    if (cached.Visual is { } kept)
                    {
                        Panel.SetZIndex(kept, e.DrawIndex);
                        _visuals[e.Block] = kept;
                    }
                    continue;
                }
                if (cached.Visual is { } old) CanvasSurface.Children.Remove(old);
            }
            _visualCache[id] = (signature, MakeVisual(e));
        }
        // What is no longer drawn.
        foreach (var (id, cached) in _visualCache.ToList())
        {
            if (seen.Contains(id)) continue;
            if (cached.Visual is { } stale) CanvasSurface.Children.Remove(stale);
            _visualCache.Remove(id);
        }

        DrawHighlights();
    }

    private bool IsShown(LaidOutElement e)
    {
        if (DocumentVisible is { } visible && !visible(e.Document)) return false;
        for (var x = e; x is not null; x = x.Parent)
            if (!x.Block.IsVisible) return false;
        return e.InActiveViewport;
    }

    /// <summary>
    /// The model a Model3D previews before any event: its Mesh, else its first MeshInfo, else the first
    /// row of the TransformNameMesh that publishes its EventMesh.
    /// </summary>
    private string? ChooseMesh(ElementSpec spec) =>
        spec.Mesh ?? spec.MeshInfos.FirstOrDefault()?.Mesh ??
        (spec.EventMesh is { } ev
            ? _graph?.Nodes.OfType<NameMeshNode>().FirstOrDefault(n => string.Equals(n.Output, ev, StringComparison.OrdinalIgnoreCase))
                ?.Rows.Select(r => r.Mesh).FirstOrDefault()
            : null);

    private static string FileName(HudDocument doc) => System.IO.Path.GetFileName(doc.SourcePath) ?? "(unsaved)";

    private static string Describe(LayoutOptions o, HudFileLayout f)
    {
        if (f.Opted)
        {
            var ratio = f.Ratio is { } r ? RatioName(r) : "?";
            var spare = o.Screen.WidthF - f.LayoutWidth;
            return $"Laid out by GameExt for {ratio}: {f.LayoutWidth:0.#} wide, full height, " +
                   (MathF.Abs(spare) < 0.5f ? "drawn as written" : $"pieces move by up to {spare:+0.#;-0.#} px");
        }
        var band = EngineMath.StockVerticalScale(o.Screen);
        var why = f.Ratio is null ? "stock layout"
            : o.Target == HudTarget.Vanilla ? "stock layout (the game ignores its FileInfo ratio)"
            : o.Viewports != 1 ? "stock layout (GameExt lays files out with one viewport)"
            : "stock layout (a 4:3 file is not laid out on a 4:3 screen)";
        return MathF.Abs(band - 1f) < 1e-4f ? why
            : $"{why}: heights in a band {band:0.###} tall, bitmaps {EngineMath.BitmapStretch(o.Screen):0.###}× taller";
    }

    internal static string RatioName(float r)
    {
        foreach (var (w, h) in new[] { (4, 3), (5, 4), (16, 9), (16, 10), (21, 9), (32, 9), (1, 1) })
            if (MathF.Abs(r - (float)w / h) < 1e-3f) return $"{w}:{h}";
        return r.ToString("0.###", CultureInfo.InvariantCulture) + ":1";
    }

    private FrameworkElement? CreateVisual(LaidOutElement e)
    {
        switch (e.Kind)
        {
            case ElementKind.Bitmap:
            case ElementKind.BitmapMasked:
            case ElementKind.BarBitmap:
            case ElementKind.ProceduralBarBitmap:
                if (e.Quad is not { } quad) return null;
                return QuadVisual(e.Spec, e.Piece?.Role == CodePieceRole.MapBackdrop ? SampleMapName : e.Texture, quad, e.Uv, e.ToScreen,
                    e.DrawColor, e.DrawAlpha, LabelFor(e), e.Mask, e.MaskUv, e.Additive);
            case ElementKind.Text:
                return TextVisual(e);
            case ElementKind.Model3D:
                return ModelVisual(e);
            case ElementKind.BarSegmented:
                return SegmentsVisual(e);
            case ElementKind.Group:
                return null;
            default:
                // Map, Target, lists and boxes: their code draws pieces of its own, which follow; the Rect is
                // outlined only when there are none.
                return ShowPlaceholders && !e.Children.Any(c => c.Piece is not null) ? FrameVisual(e) : null;
        }
    }

    private bool ShowPlaceholders => ShowPlaceholdersBox.IsChecked == true;

    private static WpfMatrix ToWpf(Matrix4x4 m) => new(m.M11, m.M12, m.M21, m.M22, m.M41, m.M42);

    private static Point ToScreen(Matrix4x4 m, double x, double y)
    {
        var p = Vector2.Transform(new Vector2((float)x, (float)y), m);
        return new Point(p.X, p.Y);
    }

    /// <summary>
    /// A textured quad: the quad in the element's own pixels, under its matrix. A quad drawn right to
    /// left or bottom to top (a FillFrom bar, a flipped TexCoords) keeps each edge's coordinate.
    /// </summary>
    /// <summary>
    /// How far down and right a Shadow style's black copy is drawn, in screen pixels: the interface shader
    /// adds (2, -2) to the clip position before the divide by w, 640 / t on the HUD's plane
    /// (<c>pcInterfaceShader::Render_Prog1</c>, P <c>0040C7DE</c>; the stock and Shader Patch vertex shaders).
    /// </summary>
    private System.Windows.Vector ShadowOffset
    {
        get
        {
            var w = EngineMath.InterfaceWidth / EngineMath.FrustumWidth;
            return new System.Windows.Vector(_screen.Width / w, _screen.Height / w);
        }
    }

    private FrameworkElement? QuadVisual(ElementSpec spec, string? textureName, QuadRect quad, TexRect uv, Matrix4x4 toScreen,
        Vector3 color, float alpha, string label, string? mask = null, TexRect? maskUv = null, bool additive = false)
    {
        var (x0, u0, x1, u1) = quad.Left <= quad.Right ? (quad.Left, uv.U0, quad.Right, uv.U1) : (quad.Right, uv.U1, quad.Left, uv.U0);
        var (y0, v0, y1, v1) = quad.Top <= quad.Bottom ? (quad.Top, uv.V0, quad.Bottom, uv.V1) : (quad.Bottom, uv.V1, quad.Top, uv.V0);
        double w = x1 - x0, h = y1 - y0;
        if (w <= 0 || h <= 0)
        {
            // No BitmapRect, or a bar at 0: the game draws nothing.
            return ShowPlaceholders && !spec.HasBitmapRect ? MarkerVisual(toScreen, label + "  (no BitmapRect)") : null;
        }

        var texture = textureName == SampleMapName ? SampleMap() : textureName is { Length: > 0 } name ? _textures?.Get(name) : null;
        FrameworkElement visual;
        if (texture is null)
        {
            if (!ShowPlaceholders) return null;
            visual = PlaceholderBox(w, h, label, color, alpha);
        }
        else
        {
            Rectangle Quad(BitmapSource source, double opacity)
            {
                var brush = new ImageBrush(source)
                {
                    Stretch = Stretch.Fill,
                    ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                    Viewbox = new Rect(Math.Min(u0, u1), Math.Min(v0, v1), Math.Max(Math.Abs(u1 - u0), 1e-6), Math.Max(Math.Abs(v1 - v0), 1e-6)),
                };
                if (u1 < u0 || v1 < v0) brush.RelativeTransform = new ScaleTransform(u1 < u0 ? -1 : 1, v1 < v0 ? -1 : 1, 0.5, 0.5);
                brush.Freeze();
                var rect = new Rectangle { Width = w, Height = h, Fill = brush, Opacity = Math.Clamp(opacity, 0.0, 1.0) };
                // A masked bitmap: the mask's alpha multiplies it, the part MaskTexCoords gives over the quad.
                if (mask is { Length: > 0 } && _textures?.Get(mask) is { } maskTexture)
                {
                    var m = maskUv ?? TexRect.Full;
                    var maskBrush = new ImageBrush(maskTexture)
                    {
                        Stretch = Stretch.Fill,
                        ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                        Viewbox = new Rect(Math.Min(m.U0, m.U1), Math.Min(m.V0, m.V1), Math.Max(Math.Abs(m.U1 - m.U0), 1e-6), Math.Max(Math.Abs(m.V1 - m.V0), 1e-6)),
                    };
                    maskBrush.Freeze();
                    rect.OpacityMask = maskBrush;
                }
                return rect;
            }

            var place = ToWpf(Matrix4x4.CreateTranslation(x0, y0, 0f) * toScreen);
            var main = Quad(Tinted(texture, color, additive), alpha);
            main.RenderTransform = new MatrixTransform(place);
            // The Shadow style: first the same quad in black at half the alpha, a little down and right on the
            // screen. Drawn additively, black adds nothing.
            if (!ElementSpec.IsShadow(spec.BitmapStyle) || additive) return main;
            var shadow = Quad(Tinted(texture, Vector3.Zero, false), Math.Floor(Math.Clamp(alpha, 0, 1) * 255) / 2 / 255);
            var offset = ShadowOffset;
            place.Translate(offset.X, offset.Y);
            shadow.RenderTransform = new MatrixTransform(place);
            var pair = new Canvas();
            pair.Children.Add(shadow);
            pair.Children.Add(main);
            return pair;
        }
        visual.RenderTransform = new MatrixTransform(ToWpf(Matrix4x4.CreateTranslation(x0, y0, 0f) * toScreen));
        return visual;
    }

    /// <summary>
    /// A text element laid out as the game lays it out (SwbfHudEditor.Core/Layout/TextLayout.cs): lines
    /// broken and clipped in its box with its font's widths, each character placed. The box is outlined
    /// with the guides.
    /// </summary>
    private FrameworkElement? TextVisual(LaidOutElement e)
    {
        var spec = e.Spec;
        // While the events decide what shows, a text that shows only what its events send stays empty
        // until one does; otherwise it shows an example until then.
        if (_showByEvents && e.Runtime is not null && e.Text is null && spec.TextKey is null) return null;
        if (ShownText(e) is not { } content)
            return ShowPlaceholders ? MarkerVisual(e.ToScreen, LabelFor(e) + "  (no text)") : null;

        var font = _fonts.For(spec.Font);
        var layout = TextLayout.Lay(content, font.Metrics, new TextLayoutOptions
        {
            BoxWidth = e.TextBoxWidth,
            BoxHeight = e.TextBoxHeight,
            ScaleX = spec.TextScale.X,
            ScaleY = spec.TextScale.Y,
            Spacing = spec.TextSpacing,
            TabSpacing = spec.TextTabSpacing,
            HAlign = spec.TextHAlign,
            VAlign = spec.TextVAlign,
            Break = spec.TextBreak,
        });
        var box = GuidesBox.IsChecked == true && (e.TextBoxWidth > 0 || e.TextBoxHeight > 0)
            ? new Rect(0, 0, e.TextBoxWidth, e.TextBoxHeight)
            : (Rect?)null;
        var shadow = ElementSpec.IsShadow(spec.TextStyle) && !e.Additive;
        var brush = new SolidColorBrush(ToColor(e.DrawColor));
        brush.Freeze();
        // The shadow's offset is on the screen; in the text's own pixels it is that through its matrix.
        var m = e.ToScreen;
        var det = m.M11 * m.M22 - m.M12 * m.M21;
        var off = ShadowOffset;
        var shadowOffset = MathF.Abs(det) < 1e-9f
            ? new System.Windows.Vector(0, 0)
            : new System.Windows.Vector((off.X * m.M22 - off.Y * m.M21) / det, (-off.X * m.M12 + off.Y * m.M11) / det);
        return new TextGlyphsVisual(layout, font, brush, shadow, shadowOffset, e.Additive, box)
        {
            Opacity = Math.Clamp(e.DrawAlpha, 0.0, 1.0),
            RenderTransform = new MatrixTransform(ToWpf(e.ToScreen)),
        };
    }

    /// <summary>What a text element shows: what its events sent, else an example of it.</summary>
    private string? ShownText(LaidOutElement e) =>
        e.Text ?? HudTextExamples.For(e.Spec, _textPreview, key => _localization?.TryGet(key));

    private FrameworkElement? ModelVisual(LaidOutElement e)
    {
        if (e.Mesh is not { } mesh || _meshRenderer?.Geometry(mesh) is null)
            return ShowPlaceholders ? MarkerVisual(e.ToScreen, LabelFor(e) + (e.Mesh is null ? "  (no mesh)" : $"  ({e.Mesh} not found)")) : null;

        // As the game draws it: its world matrix pulled toward the camera by its depth, in perspective.
        var drawing = _meshRenderer.Draw(mesh, ModelProjection.MeshToView(e.World, e.MeshDepth, _screen), _screen);
        if (drawing is null) return null;
        return new Rectangle
        {
            Width = drawing.Image.PixelWidth,
            Height = drawing.Image.PixelHeight,
            Fill = new ImageBrush(Tinted(drawing.Image, e.DrawColor, false)) { Stretch = Stretch.Fill },
            Opacity = Math.Clamp(e.DrawAlpha, 0.0, 1.0),
            RenderTransform = new TranslateTransform(drawing.Left, drawing.Top),
        };
    }

    private FrameworkElement? SegmentsVisual(LaidOutElement bar)
    {
        if (bar.SegmentSpec is not { } seg || bar.SegmentQuad is not { } quad) return ShowPlaceholders ? FrameVisual(bar) : null;
        var color = new Vector3(seg.Color.R, seg.Color.G, seg.Color.B) / 255f * bar.DrawColor;
        var alpha = seg.Alpha * (bar.Spec.PropagateAlpha ? bar.DrawAlpha : 1f);
        var panel = new Canvas { IsHitTestVisible = true };
        foreach (var s in bar.Segments)
        {
            if (!s.Lit) continue;
            if (QuadVisual(seg, seg.Texture, quad, seg.TexCoords, s.ToScreen, color, alpha, LabelFor(bar), additive: seg.Additive) is { } v)
                panel.Children.Add(v);
        }
        return panel.Children.Count > 0 ? panel : null;
    }

    /// <summary>A dashed outline of a group's Rect, for element types the preview does not draw.</summary>
    private FrameworkElement FrameVisual(LaidOutElement e)
    {
        var corners = new[] { (0.0, 0.0), (e.RectWidthPixels, 0.0), (e.RectWidthPixels, e.RectHeightPixels), (0.0, e.RectHeightPixels) };
        var polygon = new Polygon
        {
            Stroke = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(24, 180, 180, 180)),
        };
        foreach (var (x, y) in corners) polygon.Points.Add(ToScreen(e.ToScreen, x, y));
        var grid = new Canvas();
        grid.Children.Add(polygon);
        var label = SmallLabel(LabelFor(e));
        var at = ToScreen(e.ToScreen, 0, 0);
        Canvas.SetLeft(label, at.X);
        Canvas.SetTop(label, at.Y);
        grid.Children.Add(label);
        return grid;
    }

    private static FrameworkElement MarkerVisual(Matrix4x4 toScreen, string label)
    {
        var at = ToScreen(toScreen, 0, 0);
        var canvas = new Canvas();
        var box = new Rectangle
        {
            Width = 10,
            Height = 10,
            Stroke = Brushes.Orange,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 2, 2 },
            Fill = new SolidColorBrush(Color.FromArgb(40, 255, 165, 0)),
        };
        Canvas.SetLeft(box, at.X - 5);
        Canvas.SetTop(box, at.Y - 5);
        canvas.Children.Add(box);
        var text = SmallLabel(label);
        Canvas.SetLeft(text, at.X + 7);
        Canvas.SetTop(text, at.Y - 7);
        canvas.Children.Add(text);
        return canvas;
    }

    private static FrameworkElement PlaceholderBox(double w, double h, string label, Vector3 color, float alpha)
    {
        var c = ToColor(color);
        var grid = new Grid { Width = w, Height = h, Opacity = Math.Clamp(alpha, 0.2, 1.0) };
        grid.Children.Add(new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(70, c.R, c.G, c.B)),
            Stroke = new SolidColorBrush(c),
            StrokeThickness = 1,
        });
        grid.Children.Add(SmallLabel(label));
        return grid;
    }

    private static TextBlock SmallLabel(string text) => new()
    {
        Text = text,
        Foreground = Brushes.White,
        FontSize = 9,
        FontFamily = new FontFamily("Consolas, Cascadia Mono, Menlo, monospace"),
        IsHitTestVisible = false,
        Padding = new Thickness(2, 0, 2, 0),
    };

    private static string LabelFor(LaidOutElement e) => e.Piece is { } piece
        ? $"{piece.Owner.Kind}  {piece.Owner.Spec.Name ?? "(unnamed)"}: {piece.Template?.Name ?? e.Kind.ToString()}"
        : $"{e.Kind}  {e.Spec.Name ?? "(unnamed)"}";

    // ---- The map's backdrop: the level's map, which the editor has only when a level's folder is added ----

    private const string SampleMapName = "$level map";

    /// <summary>Level maps tried in turn for the map's backdrop.</summary>
    private static readonly string[] LevelMaps = { "geo1_map", "tat2_map", "kas2_map", "cor1_map", "nab2_map", "yav1_map", "dag1_map", "fel1_map" };

    private BitmapSource? _sampleMap;

    /// <summary>A level's map from the asset folders, else a drawn stand-in: terrain-like blotches under a grid.</summary>
    private BitmapSource SampleMap()
    {
        foreach (var name in LevelMaps)
            if (_textures?.Get(name) is { } found)
                return found;
        if (_sampleMap is not null) return _sampleMap;
        const int size = 256;
        var pixels = new byte[size * size * 4];
        var random = new Random(7);
        var blobs = Enumerable.Range(0, 14).Select(_ => (X: random.NextDouble() * size, Y: random.NextDouble() * size, R: 20 + random.NextDouble() * 50, Hill: random.NextDouble())).ToArray();
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var hill = 0.0;
                foreach (var b in blobs)
                {
                    var d = Math.Sqrt((x - b.X) * (x - b.X) + (y - b.Y) * (y - b.Y)) / b.R;
                    if (d < 1) hill = Math.Max(hill, (1 - d) * (0.4 + 0.6 * b.Hill));
                }
                var grid = x % 32 == 0 || y % 32 == 0;
                var i = (y * size + x) * 4;
                pixels[i] = (byte)(grid ? 170 : 100 + 50 * hill);       // B
                pixels[i + 1] = (byte)(grid ? 185 : 135 + 80 * hill);   // G
                pixels[i + 2] = (byte)(grid ? 170 : 120 + 70 * hill);   // R
                pixels[i + 3] = 255;
            }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        return _sampleMap = bitmap;
    }

    private static Color ToColor(Vector3 c) =>
        Color.FromRgb((byte)Math.Clamp(c.X * 255f + 0.5f, 0, 255), (byte)Math.Clamp(c.Y * 255f + 0.5f, 0, 255), (byte)Math.Clamp(c.Z * 255f + 0.5f, 0, 255));

    // ---- Textures: the game multiplies a texture by the element's colour ----------------------------

    private readonly Dictionary<(BitmapSource, uint, bool), BitmapSource> _tinted = new();

    /// <summary>
    /// The texture times the colour, as the interface shader draws it. Additive (<c>SrcBlend SRCALPHA</c>,
    /// <c>DestBlend ONE</c>) is a premultiplied colour with no alpha: WPF composites premultiplied, so it adds
    /// the colour times the texture's alpha (and the element's, its opacity) to what is under it.
    /// </summary>
    private BitmapSource Tinted(BitmapSource source, Vector3 color, bool additive)
    {
        var c = ToColor(color);
        var key = (source, (uint)(c.R << 16 | c.G << 8 | c.B), additive);
        if (_tinted.TryGetValue(key, out var cached)) return cached;
        if (c == Colors.White && !additive) return _tinted[key] = source;

        var w = source.PixelWidth;
        var h = source.PixelHeight;
        var bgra = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = w * 4;
        var pixels = new byte[stride * h];
        bgra.CopyPixels(pixels, stride, 0);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var b = pixels[i] * c.B / 255;
            var g = pixels[i + 1] * c.G / 255;
            var r = pixels[i + 2] * c.R / 255;
            if (additive)
            {
                var a = pixels[i + 3];
                pixels[i] = (byte)(b * a / 255);
                pixels[i + 1] = (byte)(g * a / 255);
                pixels[i + 2] = (byte)(r * a / 255);
                pixels[i + 3] = 0;
                continue;
            }
            pixels[i] = (byte)b;
            pixels[i + 1] = (byte)g;
            pixels[i + 2] = (byte)r;
        }
        var output = new WriteableBitmap(w, h, source.DpiX, source.DpiY, additive ? PixelFormats.Pbgra32 : PixelFormats.Bgra32, null);
        output.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
        output.Freeze();
        return _tinted[key] = output;
    }

    // ---- Guides ---------------------------------------------------------------------------------------

    /// <summary>One file's guides: GameExt's thirds for a file laid out on its ratio, else the stock band.</summary>
    private void DrawGuides(HudScreen s, HudFileLayout file)
    {
        var guide = new SolidColorBrush(Color.FromArgb(150, 90, 160, 220));
        var dead = new SolidColorBrush(Color.FromArgb(28, 220, 120, 80));
        if (file.Opted)
        {
            // Where each third of the layout lands, and the bands no piece without an anchor can hold.
            var lw = file.LayoutWidth;
            var spare = s.WidthF - lw;
            var edges = new[]
            {
                (0f, lw / 3f, 0f),
                (lw / 3f, lw * 2f / 3f, 0.5f * spare),
                (lw * 2f / 3f, lw, spare),
            };
            foreach (var (from, to, slide) in edges)
            {
                AddLine(from + slide, 0, from + slide, s.Height, guide, dashed: true);
                AddLine(to + slide, 0, to + slide, s.Height, guide, dashed: true);
            }
            if (spare > 0.5f)
            {
                AddRect(lw / 3f, 0, 0.5f * spare, s.Height, dead);
                AddRect(lw * 2f / 3f + 0.5f * spare, 0, 0.5f * spare, s.Height, dead);
            }
            return;
        }
        var band = EngineMath.StockVerticalScale(s);
        if (MathF.Abs(band - 1f) < 1e-4f) return;
        var top = s.HeightF * 0.5f * (1f - band);
        AddLine(0, top, s.Width, top, guide, dashed: true);
        AddLine(0, s.Height - top, s.Width, s.Height - top, guide, dashed: true);
        if (top > 0)
        {
            AddRect(0, 0, s.Width, top, dead);
            AddRect(0, s.Height - top, s.Width, top, dead);
        }
    }

    /// <summary>The reference image across the screen, under the HUD (above the guides) or over it.</summary>
    private void DrawReference()
    {
        _referenceImage = null;
        _wipeHandle = null;
        if (_reference is null || ReferenceBox.IsChecked != true) return;
        var wipe = ReferenceWipeBox.IsChecked == true;
        var image = new Image
        {
            Source = _reference,
            Width = _screen.Width,
            Height = _screen.Height,
            Stretch = Stretch.Fill,
            Opacity = ReferenceOpacitySlider.Value,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Panel.SetZIndex(image, wipe || ReferenceOnTopBox.IsChecked == true ? int.MaxValue - 1 : int.MinValue + 1);
        CanvasSurface.Children.Add(image);
        _referenceImage = image;
        if (!wipe) return;

        // The divider: a line the height of the screen, with a wider strip to grab it by.
        var handle = new Grid { Width = 14, Height = _screen.Height, Background = Brushes.Transparent, Cursor = Cursors.SizeWE };
        handle.Children.Add(new Rectangle { Width = 2, Fill = (Brush)FindResource("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Center });
        handle.Children.Add(new Ellipse
        {
            Width = 14,
            Height = 14,
            Fill = (Brush)FindResource("AccentBrush"),
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            VerticalAlignment = VerticalAlignment.Center,
        });
        ToolTipService.SetToolTip(handle, "Drag to move the divider: the reference image left of it, the HUD right of it.");
        handle.MouseLeftButtonDown += (_, e) =>
        {
            handle.CaptureMouse();
            e.Handled = true;
        };
        handle.MouseMove += (_, e) =>
        {
            if (!handle.IsMouseCaptured) return;
            _wipe = Math.Clamp(e.GetPosition(CanvasSurface).X / Math.Max(1, _screen.Width), 0, 1);
            PlaceWipe();
        };
        handle.MouseLeftButtonUp += (_, e) =>
        {
            handle.ReleaseMouseCapture();
            e.Handled = true;
        };
        Panel.SetZIndex(handle, int.MaxValue);
        CanvasSurface.Children.Add(handle);
        _wipeHandle = handle;
        PlaceWipe();
    }

    /// <summary>How much of the width the reference covers in a wipe, from the left.</summary>
    private double _wipe = 0.5;
    private Image? _referenceImage;
    private FrameworkElement? _wipeHandle;

    /// <summary>The divider and the reference's clip, moved without drawing the HUD again.</summary>
    private void PlaceWipe()
    {
        if (_referenceImage is null || _wipeHandle is null) return;
        var x = _wipe * _screen.Width;
        _referenceImage.Clip = new RectangleGeometry(new Rect(0, 0, x, _screen.Height));
        Canvas.SetLeft(_wipeHandle, x - _wipeHandle.Width / 2);
        Canvas.SetTop(_wipeHandle, 0);
    }

    private void ReferenceSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        Render();
        ReferenceSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AddLine(double x0, double y0, double x1, double y1, Brush brush, bool dashed)
    {
        var line = new Line { X1 = x0, Y1 = y0, X2 = x1, Y2 = y1, Stroke = brush, StrokeThickness = 1, IsHitTestVisible = false };
        if (dashed) line.StrokeDashArray = new DoubleCollection { 6, 4 };
        Panel.SetZIndex(line, int.MinValue);
        CanvasSurface.Children.Add(line);
    }

    private void AddRect(double x, double y, double w, double h, Brush brush)
    {
        var rect = new Rectangle { Width = Math.Max(0, w), Height = Math.Max(0, h), Fill = brush, IsHitTestVisible = false };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        Panel.SetZIndex(rect, int.MinValue);
        CanvasSurface.Children.Add(rect);
    }

    // ---- Selection highlight -------------------------------------------------------------------------

    public void HighlightBlock(HudBlock? block) =>
        HighlightBlocks(block is null ? Array.Empty<HudBlock>() : new[] { block }, block);

    // Every selected element, outlined; the current one is also marked where its position puts it.
    private IReadOnlyList<HudBlock> _selection = Array.Empty<HudBlock>();

    /// <summary>Outline the selected elements; <paramref name="current"/> is the one the Properties panel shows.</summary>
    public void HighlightBlocks(IReadOnlyList<HudBlock> blocks, HudBlock? current)
    {
        // Keep the click-cycle anchor in step with selections made elsewhere (tree, undo).
        _currentSelection = current;
        _selection = blocks;
        DrawHighlights();
    }

    private void DrawHighlights()
    {
        foreach (var h in _highlight) CanvasSurface.Children.Remove(h);
        _highlight.Clear();
        foreach (var block in _selection) DrawHighlight(block, ReferenceEquals(block, _currentSelection));
        if (_currentSelection is { } current && !_selection.Contains(current)) DrawHighlight(current, true);
    }

    /// <summary>
    /// A selected element's outline as the game draws it: its quad, text box, Rect or model; for the
    /// current one, a cross where its position puts it.
    /// </summary>
    private void DrawHighlight(HudBlock block, bool current)
    {
        if (_layout?.Find(block) is not { } e) return;
        var ring = new Polygon
        {
            Stroke = current ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x7f, 0xc3, 0xff)),
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 3, 2 },
            IsHitTestVisible = false,
        };
        foreach (var p in OutlineOf(e)) ring.Points.Add(p);
        if (ring.Points.Count > 0) AddHighlight(ring);
        if (!current) return;

        // Where its position puts it.
        var at = ToScreen(e.ToScreen, 0, 0);
        var cross = new System.Windows.Shapes.Path
        {
            Data = new GeometryGroup
            {
                Children =
                {
                    new LineGeometry(new Point(at.X - 6, at.Y), new Point(at.X + 6, at.Y)),
                    new LineGeometry(new Point(at.X, at.Y - 6), new Point(at.X, at.Y + 6)),
                },
            },
            Stroke = Brushes.Orange,
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
        };
        AddHighlight(cross);
    }

    private void AddHighlight(FrameworkElement visual)
    {
        Panel.SetZIndex(visual, int.MaxValue);
        CanvasSurface.Children.Add(visual);
        _highlight.Add(visual);
    }

    private IEnumerable<Point> OutlineOf(LaidOutElement e)
    {
        QuadRect? local = null;
        if (e.Quad is { } q && (q.Width != 0 || q.Height != 0)) local = q;
        else if (e.Kind == ElementKind.Text && (e.TextBoxWidth > 0 || e.TextBoxHeight > 0))
            local = new QuadRect(0, 0, e.TextBoxWidth, e.TextBoxHeight);
        else if (ElementKinds.IsGroupBased(e.Kind) && HasRectLine(e))
            local = new QuadRect(0, 0, e.RectWidthPixels, e.RectHeightPixels);

        if (local is { } r)
        {
            yield return ToScreen(e.ToScreen, r.Left, r.Top);
            yield return ToScreen(e.ToScreen, r.Right, r.Top);
            yield return ToScreen(e.ToScreen, r.Right, r.Bottom);
            yield return ToScreen(e.ToScreen, r.Left, r.Bottom);
            yield break;
        }

        // Otherwise the bounds of what it and its children draw.
        var bounds = Rect.Empty;
        foreach (var d in e.SelfAndDescendants())
            if (_visuals.TryGetValue(d.Block, out var v)) bounds.Union(BoundsOf(v));
        if (!bounds.IsEmpty)
        {
            yield return bounds.TopLeft;
            yield return bounds.TopRight;
            yield return bounds.BottomRight;
            yield return bounds.BottomLeft;
        }
    }

    /// <summary>A visual's extent on the canvas, worked out from its size and transform (no layout pass needed).</summary>
    private static Rect BoundsOf(FrameworkElement v)
    {
        if (v is Panel panel and not Grid)
        {
            var union = Rect.Empty;
            foreach (UIElement child in panel.Children)
                if (child is FrameworkElement fe && fe.IsHitTestVisible) union.Union(BoundsOf(fe));
            return union;
        }
        Size size;
        if (!double.IsNaN(v.Width) && !double.IsNaN(v.Height)) size = new Size(v.Width, v.Height);
        else
        {
            v.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            size = v.DesiredSize;
        }
        var left = Canvas.GetLeft(v);
        var top = Canvas.GetTop(v);
        var offset = new TranslateTransform(double.IsNaN(left) ? 0 : left, double.IsNaN(top) ? 0 : top).Value;
        var m = v.RenderTransform.Value * offset;
        return new MatrixTransform(m).TransformBounds(new Rect(size));
    }

    private static bool HasRectLine(LaidOutElement e) => e.Block.Children.Any(c => !c.HasBody && c.Name == "Rect");

    // ---- Mouse: selection and dragging ----------------------------------------------------------------

    // ---- Keyboard: the arrows nudge the selection --------------------------------------------------

    /// <summary>The arrow keys on the canvas: move the selection this many screen pixels (Shift for ten).</summary>
    public event EventHandler<System.Windows.Vector>? NudgeRequested;

    private void Canvas_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox or ComboBox) return;
        var step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
        System.Windows.Vector? by = e.Key switch
        {
            Key.Left => new System.Windows.Vector(-step, 0),
            Key.Right => new System.Windows.Vector(step, 0),
            Key.Up => new System.Windows.Vector(0, -step),
            Key.Down => new System.Windows.Vector(0, step),
            _ => null,
        };
        if (by is not { } move || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;
        NudgeRequested?.Invoke(this, move);
        e.Handled = true;   // not the scroll bars
    }

    /// <summary>
    /// The changes that move these elements by (dx, dy) screen pixels: each one's last Position line (its shown
    /// MeshInfo's for a model), through the inverse of its parent's matrix, in the line's own unit. An element
    /// inside another one moved too is left to move with it; one without a Position line stays.
    /// </summary>
    public List<ArgumentChange> MoveChanges(IEnumerable<HudBlock> blocks, double dx, double dy, bool keepKinds = false)
    {
        var changes = new List<ArgumentChange>();
        if (_layout is null) return changes;
        var elements = blocks.Select(b => _layout.Find(b)).OfType<LaidOutElement>().ToList();
        foreach (var e in elements)
        {
            if (elements.Any(o => !ReferenceEquals(o, e) && IsInside(e, o))) continue;
            var line = e.PositionOwner.Children.LastOrDefault(c => !c.HasBody && c.Name == "Position" && c.Arguments.Count >= 2);
            if (line is null || !EngineArgs.IsNumber(line.Arguments[0]) || !EngineArgs.IsNumber(line.Arguments[1])) continue;
            var delta = e.PositionDeltaFor((float)dx, (float)dy);
            for (var i = 0; i < 2; i++)
            {
                var old = line.Arguments[i];
                var value = EngineArgs.Float(old) + (i == 0 ? delta.X : delta.Y);
                // A whole number stays whole for a drag; a nudge or an alignment takes the fraction.
                var kind = !keepKinds && old.Kind == HudArgKind.Integer && value != Math.Round(value) ? HudArgKind.Float : old.Kind;
                var changed = value == EngineArgs.Float(old) ? old
                    : new HudArgument(kind, kind == HudArgKind.Integer ? FormatForKind(value, kind) : HudArgument.FromFloat(value).Raw);
                // Both numbers, moved or not, so a run of nudges in any direction joins one undo step.
                changes.Add(new ArgumentChange(line, i, old, changed));
            }
        }
        return changes;
    }

    private static bool IsInside(LaidOutElement e, LaidOutElement container)
    {
        for (var p = e.Parent; p is not null; p = p.Parent)
            if (ReferenceEquals(p, container)) return true;
        return false;
    }

    // ---- The cursor's place on the screen --------------------------------------------------------------

    private void ShowCursor(Point at)
    {
        if (at.X < 0 || at.Y < 0 || at.X > _screen.Width || at.Y > _screen.Height)
        {
            CursorText.Text = string.Empty;
            return;
        }
        CursorText.Text = string.Create(CultureInfo.InvariantCulture,
            $"{at.X:0.#}, {at.Y:0.#} px   {at.X / _screen.Width:0.0000}, {at.Y / _screen.Height:0.0000}");
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Focus();   // the arrow keys nudge what is picked here
        var pos = e.GetPosition(CanvasSurface);
        var stack = HitTestAllBlocks(pos);
        if (stack.Count == 0) return;

        // Same spot again with the selection in the stack: cycle on release unless this turns into a drag.
        var sameSpot = Math.Abs(pos.X - _lastDownPos.X) < CycleTolerancePx && Math.Abs(pos.Y - _lastDownPos.Y) < CycleTolerancePx;
        var canCycle = sameSpot && _currentSelection is not null && stack.Contains(_currentSelection);

        HudBlock target;
        _pickOnRelease = null;
        if (canCycle)
        {
            target = _currentSelection!;
            _clickCycleArmed = true;
        }
        else
        {
            target = stack[0];
            _clickCycleArmed = false;
            if (_selection.Count > 1 && _selection.Contains(target) && (Keyboard.Modifiers & ModifierKeys.Control) == 0)
                _pickOnRelease = target;
            else
            {
                _currentSelection = target;
                BlockSelected?.Invoke(this, target);
            }
        }

        _lastDownPos = pos;
        _dragBlock = target;
        _dragStartMouse = pos;
        _isDragging = false;
        CanvasSurface.CaptureMouse();
        e.Handled = true;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragBlock is null || !CanvasSurface.IsMouseCaptured) return;
        var current = e.GetPosition(CanvasSurface);
        var dx = current.X - _dragStartMouse.X;
        var dy = current.Y - _dragStartMouse.Y;
        if (!_isDragging)
        {
            if (Math.Abs(dx) < DragThresholdPx && Math.Abs(dy) < DragThresholdPx) return;
            _isDragging = true;
            StartDrag();
        }
        DragBy(dx, dy, Keyboard.Modifiers);
    }

    /// <summary>Start dragging an element, as a press on it and a move do: the tests' way in.</summary>
    internal void BeginDrag(HudBlock pressed)
    {
        _dragBlock = pressed;
        _isDragging = true;
        StartDrag();
    }

    /// <summary>
    /// Move what is dragged by (dx, dy) screen pixels from where it started. Shift keeps to the axis moved
    /// along most; an edge or the centre snaps to others' unless Alt is held.
    /// </summary>
    internal void DragBy(double dx, double dy, ModifierKeys modifiers)
    {
        if (_dragBlock is null || _dragOriginals.Count == 0) return;
        var across = true;
        var down = true;
        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            if (Math.Abs(dx) >= Math.Abs(dy)) (dy, down) = (0, false);
            else (dx, across) = (0, false);
        }
        (double? X, double? Y) met = (null, null);
        if ((modifiers & ModifierKeys.Alt) != 0) _altInDrag = true;
        else met = Snap(ref dx, ref dy, across, down);
        Guides = met;

        // From where they were, each time: the numbers keep their kind, as a drag of one always has.
        foreach (var o in _dragOriginals) o.Line.Arguments[o.Index] = o.Old;
        foreach (var c in MoveChanges(_dragBlocks, dx, dy, keepKinds: true)) c.Line.Arguments[c.Index] = c.New;
        _reuseVisuals = true;
        Render();
        DrawGuides(met.X, met.Y);
        BlockDragged?.Invoke(this, _dragBlock);
    }

    /// <summary>
    /// A drag begins: what it moves (the whole selection when the element pressed is in it), their numbers as
    /// they are, and the lines they can snap to: the edges and centres of every other element drawn, and the
    /// screen's edges and centre lines.
    /// </summary>
    private void StartDrag()
    {
        _altInDrag = false;
        _pickOnRelease = null;
        _dragBlocks = _selection.Count > 1 && _selection.Contains(_dragBlock!) ? _selection.ToList() : new List<HudBlock> { _dragBlock! };
        _dragOriginals = MoveChanges(_dragBlocks, 0, 0);
        var moving = _dragBlocks.Select(b => _layout?.Find(b)).OfType<LaidOutElement>().ToList();
        _dragStartBounds = Rect.Empty;
        foreach (var m in moving) _dragStartBounds.Union(ScreenBounds(m));
        _snapXs.Clear();
        _snapYs.Clear();
        _snapXs.AddRange(new[] { 0, _screen.Width / 2.0, _screen.Width });
        _snapYs.AddRange(new[] { 0, _screen.Height / 2.0, _screen.Height });
        if (_layout is null) return;
        foreach (var other in _layout.All)
        {
            if (!_visuals.ContainsKey(other.Block) || moving.Any(m => ReferenceEquals(m, other) || IsInside(other, m))) continue;
            var b = ScreenBounds(other);
            if (b.IsEmpty) continue;
            _snapXs.AddRange(new[] { b.Left, b.Left + b.Width / 2, b.Right });
            _snapYs.AddRange(new[] { b.Top, b.Top + b.Height / 2, b.Bottom });
        }
    }

    /// <summary>
    /// Shift (dx, dy) so an edge or the centre of what is dragged meets the nearest line within reach (a few
    /// pixels of the view, whatever the zoom); the lines met, to draw.
    /// </summary>
    private (double? X, double? Y) Snap(ref double dx, ref double dy, bool across, bool down)
    {
        if (_dragStartBounds.IsEmpty) return (null, null);
        var reach = SnapReach;
        var b = _dragStartBounds;
        var x = across ? Nearest(new[] { b.Left, b.Left + b.Width / 2, b.Right }, dx, _snapXs, reach) : null;
        var y = down ? Nearest(new[] { b.Top, b.Top + b.Height / 2, b.Bottom }, dy, _snapYs, reach) : null;
        if (x is { } sx) dx += sx.Shift;
        if (y is { } sy) dy += sy.Shift;
        return (x?.Line, y?.Line);
    }

    private static (double Line, double Shift)? Nearest(double[] features, double moved, List<double> lines, double reach)
    {
        (double Line, double Shift)? best = null;
        foreach (var feature in features)
            foreach (var line in lines)
            {
                var shift = line - (feature + moved);
                if (Math.Abs(shift) <= reach && (best is null || Math.Abs(shift) < Math.Abs(best.Value.Shift))) best = (line, shift);
            }
        return best;
    }

    private static Brush GuideColour()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xff, 0x4f, 0xd8));
        brush.Freeze();
        return brush;
    }

    /// <summary>The lines the drag snapped to last: a screen x, a screen y.</summary>
    internal (double? X, double? Y) Guides { get; private set; }

    /// <summary>How far, in screen pixels, an edge snaps from at this zoom.</summary>
    internal double SnapReach => SnapPx / Math.Max(ZoomTransform.ScaleX, 0.01);

    /// <summary>The lines a drag snapped to, across the screen; none to clear them.</summary>
    private void DrawGuides(double? x, double? y)
    {
        foreach (var guide in _guides) CanvasSurface.Children.Remove(guide);
        _guides.Clear();
        var thickness = 1 / Math.Max(ZoomTransform.ScaleX, 0.01);
        if (x is { } gx) AddGuide(new Line { X1 = gx, Y1 = 0, X2 = gx, Y2 = _screen.Height });
        if (y is { } gy) AddGuide(new Line { X1 = 0, Y1 = gy, X2 = _screen.Width, Y2 = gy });

        void AddGuide(Line line)
        {
            line.Stroke = GuideBrush;
            line.StrokeThickness = thickness;
            line.IsHitTestVisible = false;
            Panel.SetZIndex(line, int.MaxValue);
            CanvasSurface.Children.Add(line);
            _guides.Add(line);
        }
    }

    /// <summary>An element's extent on the screen: the box round the outline drawn when it is selected.</summary>
    internal Rect ScreenBounds(LaidOutElement e)
    {
        var bounds = Rect.Empty;
        foreach (var p in OutlineOf(e)) bounds.Union(p);
        return bounds;
    }

    /// <summary>A block's extent on the screen, if it is laid out and draws or has a size.</summary>
    public Rect? ScreenBoundsOf(HudBlock block) =>
        _layout?.Find(block) is { } e && ScreenBounds(e) is { IsEmpty: false } bounds ? bounds : null;

    /// <summary>Alt released after a drag it turned snapping off for: not to the menu.</summary>
    private void Canvas_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_altInDrag || e.Key != Key.System || e.SystemKey is not (Key.LeftAlt or Key.RightAlt)) return;
        _altInDrag = false;
        e.Handled = true;
    }

    /// <summary>End a drag: what it changed goes to the undo history as one step.</summary>
    internal void EndDrag()
    {
        var block = _dragBlock;
        var originals = _dragOriginals;
        _dragBlock = null;
        _pickOnRelease = null;
        _dragOriginals = new List<ArgumentChange>();
        _isDragging = false;
        _clickCycleArmed = false;
        Guides = (null, null);
        DrawGuides(null, null);
        var changes = originals.Select(o => o with { New = o.Line.Arguments[o.Index] }).Where(c => c.New != c.Old).ToList();
        if (block is null || changes.Count == 0) return;
        var moved = changes.Select(c => c.Line).Distinct().Count();
        BlockDragCommitted?.Invoke(this, new ArgumentsEdit(changes, moved == 1 ? $"move {block.Name}" : $"move {moved} elements", block));
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (CanvasSurface.IsMouseCaptured) CanvasSurface.ReleaseMouseCapture();
        if (_isDragging)
        {
            EndDrag();
            return;
        }
        var pick = _pickOnRelease;
        _dragBlock = null;
        _pickOnRelease = null;
        _dragOriginals = new List<ArgumentChange>();

        if (pick is not null)
        {
            _currentSelection = pick;
            BlockSelected?.Invoke(this, pick);
            return;
        }

        if (_clickCycleArmed && _currentSelection is not null)
        {
            var stack = HitTestAllBlocks(e.GetPosition(CanvasSurface));
            var idx = stack.IndexOf(_currentSelection);
            if (idx >= 0 && stack.Count > 1)
            {
                var next = stack[(idx + 1) % stack.Count];
                _currentSelection = next;
                BlockSelected?.Invoke(this, next);
                HoverInfoChanged?.Invoke(this, $"Selected {next.Name} \"{FirstArgName(next)}\": {idx + 2} of {stack.Count} here (click again to cycle)");
            }
        }
        _clickCycleArmed = false;
    }

    private static string FirstArgName(HudBlock block) =>
        block.Arguments.Count > 0 && block.Arguments[0].Kind == HudArgKind.String ? block.Arguments[0].Raw : "(unnamed)";

    /// <summary>Every block whose visual is under the point, topmost first.</summary>
    private List<HudBlock> HitTestAllBlocks(Point pos)
    {
        var hits = new List<HudBlock>();
        VisualTreeHelper.HitTest(CanvasSurface, null, result =>
        {
            for (var node = result.VisualHit; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is FrameworkElement { Tag: HudBlock block })
                {
                    if (!hits.Contains(block)) hits.Add(block);
                    break;
                }
            }
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(pos));
        return hits;
    }

    private static string FormatForKind(double n, HudArgKind kind) => kind switch
    {
        HudArgKind.Integer => ((long)Math.Round(n)).ToString(CultureInfo.InvariantCulture),
        _ => n.ToString("0.000000", CultureInfo.InvariantCulture),
    };

    private void Visual_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: HudBlock block } || _layout?.Find(block) is not { } el) return;
        var spec = el.Spec;
        var pos = el.MeshInfo is { } mi
            ? $"pos ({mi.Position.X:0.###}, {mi.Position.Y:0.###}) {mi.PositionMode} via MeshInfo {mi.Mesh}"
            : spec.HasPosition ? $"pos ({spec.Position.X:0.###}, {spec.Position.Y:0.###}) {spec.PositionMode}" : "no Position";
        var at = ToScreen(el.ToScreen, 0, 0);
        var piece = el.Role switch
        {
            PieceRole.Piece => el.Spec.ScreenAnchorShare is { } share
                ? $"    piece, ScreenAnchor {share:0.##}, slid {el.Slide:+0.#;-0.#;0}"
                : $"    piece, {el.Third} third, slid {el.Slide:+0.#;-0.#;0}",
            PieceRole.Container => "    plain container",
            PieceRole.Target => "    Target, never slid",
            _ => string.Empty,
        };
        var stack = HitTestAllBlocks(e.GetPosition(CanvasSurface));
        var stackIdx = stack.IndexOf(block);
        var stackHint = stack.Count > 1 && stackIdx >= 0 ? $"    [{stackIdx + 1} of {stack.Count} here; click to cycle]" : string.Empty;
        HoverInfoChanged?.Invoke(this, $"{el.Kind} \"{spec.Name ?? "(unnamed)"}\"    {pos}    screen ({at.X:0.#}, {at.Y:0.#}){piece}{stackHint}");
    }

    private void Visual_MouseLeave(object sender, MouseEventArgs e) => HoverInfoChanged?.Invoke(this, null);

    // ---- Toolbar ------------------------------------------------------------------------------------

    private void ApplyScreen()
    {
        if (!_ready) return;
        ViewportFrame.Width = _screen.Width + 2;
        ViewportFrame.Height = _screen.Height + 2;
        CanvasSurface.Width = _screen.Width;
        CanvasSurface.Height = _screen.Height;
        Render();
        _fitOnNextLayout = true;
        FitIfPending();
    }

    private void FitIfPending()
    {
        if (!_fitOnNextLayout || !IsLoaded) return;
        _fitOnNextLayout = false;
        Dispatcher.BeginInvoke(new Action(Fit), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Fit()
    {
        var availableW = Scroller.ViewportWidth - 90;
        var availableH = Scroller.ViewportHeight - 90;
        if (availableW <= 0 || availableH <= 0) return;
        SetZoom(Math.Min(availableW / _screen.Width, availableH / _screen.Height));
    }

    private void SetZoom(double scale)
    {
        scale = Math.Clamp(scale, 0.05, 4.0);
        ZoomTransform.ScaleX = scale;
        ZoomTransform.ScaleY = scale;
        ZoomText.Text = $"{scale * 100:0}%";
    }

    private void Fit_Click(object sender, RoutedEventArgs e) => Fit();

    private void ActualSize_Click(object sender, RoutedEventArgs e) => SetZoom(1.0);

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(ZoomTransform.ScaleX * 1.25);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(ZoomTransform.ScaleX / 1.25);

    private void MorePopup_Closed(object? sender, EventArgs e) => MoreButton.IsChecked = false;

    private void TargetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _target = TargetCombo.SelectedIndex == 1 ? HudTarget.Vanilla : HudTarget.GameExt;
        // Each game's own rounding: the stock draw snaps, GameExt's HudSubPixel does not.
        _ready = false;
        RoundingBox.IsChecked = DefaultRounding(_target);
        _ready = true;
        Render();
    }

    private void ScreenCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScreenCombo.SelectedItem is ComboBoxItem { Tag: HudScreen screen })
        {
            _screen = screen;
            ApplyScreen();
        }
    }

    private void ViewportCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _activeViewport = ViewportCombo.SelectedIndex + 1;
        Render();
    }

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        Render();
        if (_ready && (ReferenceEquals(sender, ReferenceBox) || ReferenceEquals(sender, ReferenceOnTopBox)))
            ReferenceSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TextPreviewCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _textPreview = (TextPreview)Math.Max(0, TextPreviewCombo.SelectedIndex);
        Render();
    }

    private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        HudScaleLabel.Text = HudScaleSlider.Value.ToString("0.00", CultureInfo.InvariantCulture);
        Render();
    }

    private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ZoomAt(ZoomTransform.ScaleX * (e.Delta > 0 ? 1.1 : 1.0 / 1.1), e.GetPosition(Scroller));
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }

    /// <summary>Zoom keeping the screen point under <paramref name="at"/> (in the scroller) where it is.</summary>
    private void ZoomAt(double scale, Point at)
    {
        var anchor = Scroller.TranslatePoint(at, ViewportFrame);
        SetZoom(scale);
        Scroller.UpdateLayout();
        var now = ViewportFrame.TranslatePoint(anchor, Scroller);
        Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset + now.X - at.X);
        Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + now.Y - at.Y);
    }

    private Point? _panFrom;
    private Point _panOffset;

    // The middle button drags the view.
    private void Scroller_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || !Scroller.CaptureMouse()) return;
        _panFrom = e.GetPosition(Scroller);
        _panOffset = new Point(Scroller.HorizontalOffset, Scroller.VerticalOffset);
        Scroller.Cursor = Cursors.ScrollAll;
        e.Handled = true;
    }

    private void Scroller_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        ShowCursor(e.GetPosition(CanvasSurface));
        if (_panFrom is not { } from) return;
        var delta = e.GetPosition(Scroller) - from;
        Scroller.ScrollToHorizontalOffset(_panOffset.X - delta.X);
        Scroller.ScrollToVerticalOffset(_panOffset.Y - delta.Y);
        e.Handled = true;
    }

    private void Scroller_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || _panFrom is null) return;
        EndPan();
        e.Handled = true;
    }

    private void EndPan()
    {
        _panFrom = null;
        Scroller.Cursor = null;
        if (Scroller.IsMouseCaptured) Scroller.ReleaseMouseCapture();
    }

    private void Scroller_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_panFrom is not null) EndPan();
    }
}
