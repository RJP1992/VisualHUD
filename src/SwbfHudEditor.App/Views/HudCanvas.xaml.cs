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

    // Drag state
    private HudBlock? _dragBlock;
    private LaidOutElement? _dragElement;
    private Point _dragStartMouse;
    private HudBlock? _dragPositionBlock;
    private HudArgument? _dragOriginalX;
    private HudArgument? _dragOriginalY;
    private Vector2 _dragStartValue;
    private bool _isDragging;
    private const double DragThresholdPx = 3;

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
        if (_activeDocument is not null && !docs.Contains(_activeDocument)) _activeDocument = null;
        _textures = assets is not null ? new TextureCache(assets) : null;
        _fonts = new TextFonts(assets);
        _meshRenderer = _textures is not null && assets is not null ? new MeshRenderer(assets, _textures) : null;
        if (_meshRenderer is not null) _meshRenderer.MeshUnitToHudPixels = MeshFactorSlider.Value;
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
        if (_events is not null) _events.Changed -= Events_Changed;
        _events = events;
        if (_events is not null) _events.Changed += Events_Changed;
        Render();
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
            Render();
        }
    }

    private void Events_Changed(object? sender, EventArgs e) => Render();

    /// <summary>Re-render without re-parsing or re-resolving assets. Call after an edit or a visibility change.</summary>
    public void Refresh() => Render();

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
            if (e.Kind == ElementKind.Model3D) what.Add($"mesh {e.Mesh ?? "(none)"}{(e.Mesh is { } m && _meshRenderer?.Get(m) is null ? " (not found)" : "")}");
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

    private void Render()
    {
        if (!_ready) return;
        CanvasSurface.Children.Clear();
        _visuals.Clear();
        _highlight.Clear();
        _layout = null;
        if (_documents.Count == 0)
        {
            LayoutInfoText.Text = string.Empty;
            LayoutInfoText.ToolTip = null;
            return;
        }

        _graph = HudTransformGraph.From(_documents);
        HudSimulation? simulation = null;
        if (_events is not null && (_showByEvents || _events.Sends.Count > 0))
            simulation = HudSimulation.Run(_documents, _graph, _events, _localization is { } table ? table.TryGetByHash : null);
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
        foreach (var e in _layout.All.OrderBy(e => e.DrawIndex))
        {
            if (!IsShown(e)) continue;
            // An element its events have not enabled is not drawn, or drawn faintly to keep it in reach.
            if (!e.Shown && !showHidden) continue;
            var visual = CreateVisual(e);
            if (visual is null) continue;
            if (!e.Shown) visual.Opacity *= 0.2;
            visual.Tag = e.Block;
            visual.MouseEnter += Visual_MouseEnter;
            visual.MouseLeave += Visual_MouseLeave;
            visual.Cursor = Cursors.SizeAll;
            ToolTipService.SetToolTip(visual, e.Block.ToString());
            Panel.SetZIndex(visual, e.DrawIndex);
            CanvasSurface.Children.Add(visual);
            _visuals[e.Block] = visual;
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
                return e.Quad is { } quad
                    ? QuadVisual(e.Spec, e.Texture, quad, e.Uv, e.ToScreen, e.DrawColor, e.DrawAlpha, LabelFor(e))
                    : null;
            case ElementKind.Text:
                return TextVisual(e);
            case ElementKind.Model3D:
                return ModelVisual(e);
            case ElementKind.BarSegmented:
                return SegmentsVisual(e);
            case ElementKind.Group:
                return null;
            default:
                // Map, Target, lists and boxes: drawn by code the preview does not run yet.
                return ShowPlaceholders ? FrameVisual(e) : null;
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
    private FrameworkElement? QuadVisual(ElementSpec spec, string? textureName, QuadRect quad, TexRect uv, Matrix4x4 toScreen,
        Vector3 color, float alpha, string label)
    {
        var (x0, u0, x1, u1) = quad.Left <= quad.Right ? (quad.Left, uv.U0, quad.Right, uv.U1) : (quad.Right, uv.U1, quad.Left, uv.U0);
        var (y0, v0, y1, v1) = quad.Top <= quad.Bottom ? (quad.Top, uv.V0, quad.Bottom, uv.V1) : (quad.Bottom, uv.V1, quad.Top, uv.V0);
        double w = x1 - x0, h = y1 - y0;
        if (w <= 0 || h <= 0)
        {
            // No BitmapRect, or a bar at 0: the game draws nothing.
            return ShowPlaceholders && !spec.HasBitmapRect ? MarkerVisual(toScreen, label + "  (no BitmapRect)") : null;
        }

        var texture = textureName is { Length: > 0 } name ? _textures?.Get(name) : null;
        FrameworkElement visual;
        if (texture is null)
        {
            if (!ShowPlaceholders) return null;
            visual = PlaceholderBox(w, h, label, color, alpha);
        }
        else
        {
            var source = Tinted(texture, color, spec.Additive);
            var brush = new ImageBrush(source)
            {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                Viewbox = new Rect(Math.Min(u0, u1), Math.Min(v0, v1), Math.Max(Math.Abs(u1 - u0), 1e-6), Math.Max(Math.Abs(v1 - v0), 1e-6)),
            };
            if (u1 < u0 || v1 < v0) brush.RelativeTransform = new ScaleTransform(u1 < u0 ? -1 : 1, v1 < v0 ? -1 : 1, 0.5, 0.5);
            brush.Freeze();
            visual = new Rectangle { Width = w, Height = h, Fill = brush, Opacity = Math.Clamp(alpha, 0.0, 1.0) };
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
        var shadow = spec.TextStyle is { } style && style.StartsWith("Shadow", StringComparison.OrdinalIgnoreCase);
        var brush = new SolidColorBrush(ToColor(e.DrawColor));
        brush.Freeze();
        return new TextGlyphsVisual(layout, font, brush, shadow, box)
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
        var result = e.Mesh is { } mesh ? _meshRenderer?.Get(mesh) : null;
        if (result is null)
            return ShowPlaceholders ? MarkerVisual(e.ToScreen, LabelFor(e) + (e.Mesh is null ? "  (no mesh)" : $"  ({e.Mesh} not found)")) : null;

        // Estimated: a mesh unit drawn as 1/7 of the screen's width (see MeshRenderer).
        var unit = _screen.Width / 7.0 * (_meshRenderer?.MeshUnitToHudPixels ?? 1.0);
        var w = result.WidthUnits * unit;
        var h = result.HeightUnits * unit;
        if (w <= 0 || h <= 0) return null;
        var image = new Rectangle
        {
            Width = w,
            Height = h,
            Fill = new ImageBrush(Tinted(result.Image, e.DrawColor, false)) { Stretch = Stretch.Fill },
            Opacity = Math.Clamp(e.DrawAlpha, 0.0, 1.0),
        };
        // The mesh's origin sits at the element's position.
        var local = Matrix4x4.CreateTranslation((float)(-result.OriginFracX * w), (float)(-result.OriginFracY * h), 0f);
        image.RenderTransform = new MatrixTransform(ToWpf(local * e.ToScreen));
        return image;
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
            if (QuadVisual(seg, seg.Texture, quad, seg.TexCoords, s.ToScreen, color, alpha, LabelFor(bar)) is { } v) panel.Children.Add(v);
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

    private static string LabelFor(LaidOutElement e) => $"{e.Kind}  {e.Spec.Name ?? "(unnamed)"}";

    private static Color ToColor(Vector3 c) =>
        Color.FromRgb((byte)Math.Clamp(c.X * 255f + 0.5f, 0, 255), (byte)Math.Clamp(c.Y * 255f + 0.5f, 0, 255), (byte)Math.Clamp(c.Z * 255f + 0.5f, 0, 255));

    // ---- Textures: the game multiplies a texture by the element's colour ----------------------------

    private readonly Dictionary<(BitmapSource, uint, bool), BitmapSource> _tinted = new();

    /// <summary>
    /// The texture times the colour, as the interface shader draws it. Additive blending has no WPF
    /// equivalent: its brightness becomes alpha, so a glow on black reads as the glow.
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
            pixels[i] = (byte)b;
            pixels[i + 1] = (byte)g;
            pixels[i + 2] = (byte)r;
            if (additive) pixels[i + 3] = (byte)(Math.Max(r, Math.Max(g, b)) * pixels[i + 3] / 255);
        }
        var output = new WriteableBitmap(w, h, source.DpiX, source.DpiY, PixelFormats.Bgra32, null);
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
        if (_reference is null || ReferenceBox.IsChecked != true) return;
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
        Panel.SetZIndex(image, ReferenceOnTopBox.IsChecked == true ? int.MaxValue - 1 : int.MinValue + 1);
        CanvasSurface.Children.Add(image);
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

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(CanvasSurface);
        var stack = HitTestAllBlocks(pos);
        if (stack.Count == 0) return;

        // Same spot again with the selection in the stack: cycle on release unless this turns into a drag.
        var sameSpot = Math.Abs(pos.X - _lastDownPos.X) < CycleTolerancePx && Math.Abs(pos.Y - _lastDownPos.Y) < CycleTolerancePx;
        var canCycle = sameSpot && _currentSelection is not null && stack.Contains(_currentSelection);

        HudBlock target;
        if (canCycle)
        {
            target = _currentSelection!;
            _clickCycleArmed = true;
        }
        else
        {
            target = stack[0];
            _currentSelection = target;
            BlockSelected?.Invoke(this, target);
            _clickCycleArmed = false;
        }

        _lastDownPos = pos;
        _dragBlock = target;
        _dragElement = _layout?.Find(target);
        _dragStartMouse = pos;
        // The engine reads the last Position line, so that is the one a drag moves.
        _dragPositionBlock = _dragElement?.PositionOwner.Children.LastOrDefault(c => !c.HasBody && c.Name == "Position");
        if (_dragPositionBlock is { } p && p.Arguments.Count >= 2)
        {
            _dragOriginalX = p.Arguments[0];
            _dragOriginalY = p.Arguments[1];
            _dragStartValue = new Vector2(EngineArgs.Float(p.Arguments[0]), EngineArgs.Float(p.Arguments[1]));
        }
        else
        {
            _dragPositionBlock = null;
            _dragOriginalX = null;
            _dragOriginalY = null;
        }

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
        if (!_isDragging && Math.Abs(dx) < DragThresholdPx && Math.Abs(dy) < DragThresholdPx) return;
        _isDragging = true;

        if (_dragElement is null || _dragPositionBlock is null) return;
        // The inverse of the matrix its parent hands it, then of its Position's mode.
        var delta = _dragElement.PositionDeltaFor((float)dx, (float)dy);
        var p = _dragPositionBlock;
        p.Arguments[0] = p.Arguments[0] with { Raw = FormatForKind(_dragStartValue.X + delta.X, p.Arguments[0].Kind) };
        p.Arguments[1] = p.Arguments[1] with { Raw = FormatForKind(_dragStartValue.Y + delta.Y, p.Arguments[1].Kind) };
        Render();
        BlockDragged?.Invoke(this, _dragBlock);
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (CanvasSurface.IsMouseCaptured) CanvasSurface.ReleaseMouseCapture();
        var wasDragging = _isDragging;
        var block = _dragBlock;
        var posBlock = _dragPositionBlock;
        var origX = _dragOriginalX;
        var origY = _dragOriginalY;

        _dragBlock = null;
        _dragElement = null;
        _dragPositionBlock = null;
        _dragOriginalX = null;
        _dragOriginalY = null;
        _isDragging = false;

        if (wasDragging)
        {
            _clickCycleArmed = false;
            if (block is null || posBlock is null || origX is null || origY is null) return;
            var edits = new List<IHudEdit>(2)
            {
                new ReplaceArgumentEdit(posBlock, 0, origX, posBlock.Arguments[0], affected: block),
                new ReplaceArgumentEdit(posBlock, 1, origY, posBlock.Arguments[1], affected: block),
            };
            BlockDragCommitted?.Invoke(this, new CompositeEdit(edits, $"move {block.Name}"));
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
        MeshFactorLabel.Text = MeshFactorSlider.Value.ToString("0.00", CultureInfo.InvariantCulture);
        if (_meshRenderer is not null) _meshRenderer.MeshUnitToHudPixels = MeshFactorSlider.Value;
        Render();
    }

    private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        SetZoom(ZoomTransform.ScaleX * (e.Delta > 0 ? 1.1 : 1.0 / 1.1));
        e.Handled = true;
    }
}
