using System.Numerics;
using SwbfHudEditor.Core.Assets;
using SwbfHudEditor.Core.Events;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Layout;

/// <summary>
/// The pieces the game builds in code for BorderedBox, ObjectiveList, MultilineText, VehicleSeating,
/// Target and Map, laid out as that code lays them out (docs/Engine.md, "Code-built elements"). Where
/// they show the game's state, <see cref="CodeSamples"/> stands in for it.
/// </summary>
public static partial class HudLayout
{
    private static void AddCodePieces(Context ctx, LaidOutElement e)
    {
        switch (e.Kind)
        {
            case ElementKind.BorderedBox: BorderedBox(ctx, e); break;
            case ElementKind.ObjectiveList: ObjectiveList(ctx, e); break;
            case ElementKind.MultilineText: MultilineText(ctx, e); break;
            case ElementKind.VehicleSeating: VehicleSeating(ctx, e); break;
            case ElementKind.Target: Target(ctx, e); break;
            case ElementKind.Map: Map(ctx, e); break;
            case ElementKind.BarBitmap when e.Runtime?.Clock?.Strip is { } strip: BarStrip(e, strip); break;
        }
    }

    /// <summary>
    /// A BarBitmap's change strip (<c>ElementBarBitmap::SetValue</c>, P <c>005F7920</c>, and Update, P <c>005F7C30</c>):
    /// the bar's texture in white over the part of the bar the last change uncovered, fading out. A shrinking
    /// strip's top comes down to <c>FlashyScale</c> pixels above its bottom over the first second.
    /// </summary>
    private static void BarStrip(LaidOutElement bar, Events.StripState strip)
    {
        if (bar.FullQuad is not { } full) return;
        var uv = bar.Spec.TexCoords;
        var flashyScale = Line(bar.Block, "FlashyScale") is { } fs ? EngineArgs.FloatAt(fs, 0) : 0.2f;
        float x0 = full.Left + strip.From * full.Width, x1 = full.Left + strip.To * full.Width;
        float u0 = uv.U0 + strip.From * (uv.U1 - uv.U0), u1 = uv.U0 + strip.To * (uv.U1 - uv.U0);
        var h = full.Height * (1f - strip.Collapse) + strip.Collapse * flashyScale;
        var hv = (uv.V1 - uv.V0) * (1f - strip.Collapse) + strip.Collapse * flashyScale;
        var group = bar.Parent;
        var piece = AddPiece(bar, BareSpec(ElementKind.Bitmap), null, bar.Spec.ZOrder != 0 ? bar.Spec.ZOrder - 1 : 0,
            Matrix4x4.Identity, Vector3.One, strip.Alpha);
        // White, tinted by the bar's group rather than the bar; the strip's own alpha, through the group's.
        piece.DrawColor = group?.DrawColor ?? Vector3.One;
        piece.DrawAlpha = strip.Alpha * (group is { Spec.PropagateAlpha: true } ? group.DrawAlpha : 1f);
        piece.Quad = piece.FullQuad = new QuadRect(MathF.Min(x0, x1), full.Bottom - h, MathF.Max(x0, x1), full.Bottom);
        piece.Uv = new TexRect(MathF.Min(u0, u1), uv.V1 - hv, MathF.Max(u0, u1), uv.V1);
        piece.Texture = bar.Texture;
    }

    // ---- Pieces ----------------------------------------------------------------------------------

    /// <summary>
    /// A piece of <paramref name="owner"/> drawn as <paramref name="spec"/> says, at <paramref name="local"/> in
    /// the owner's pixels. A group tints every child's colour, and passes its alpha on only with
    /// <c>PropagateAlpha(1)</c>.
    /// </summary>
    private static LaidOutElement AddPiece(LaidOutElement owner, ElementSpec spec, HudBlock? template, int zOrder,
        Matrix4x4 local, Vector3 color, float alpha, CodePieceRole role = CodePieceRole.Other)
    {
        var p = new LaidOutElement
        {
            Spec = spec,
            Parent = owner,
            Depth = owner.Depth + 1,
            Document = owner.Document,
            FileLayout = owner.FileLayout,
            Piece = new CodePiece(owner, template, zOrder, role),
            Viewport = owner.Viewport,
            ViewWidth = owner.ViewWidth,
            ViewHeight = owner.ViewHeight,
            ScreenWidth = owner.ScreenWidth,
            ScreenHeight = owner.ScreenHeight,
            ScreenPixelsWide = owner.ScreenPixelsWide,
        };
        p.InActiveViewport = owner.InActiveViewport;
        p.Shown = owner.Shown;
        p.FrameWidth = owner.RectWidthPixels;
        p.FrameHeight = owner.RectHeightPixels;
        p.Local = p.LoadLocal = local;
        p.DrawColor = color * owner.DrawColor;
        p.DrawAlpha = alpha * (owner.Spec.PropagateAlpha ? owner.DrawAlpha : 1f);
        owner.Children.Add(p);
        return p;
    }

    /// <summary>A spec for a piece with no template: a drawable as the code makes it, white, z-order 128.</summary>
    private static ElementSpec BareSpec(ElementKind kind) => ElementReader.Read(new HudBlock(kind.ToString()), kind);

    private static ElementSpec TemplateSpec(HudBlock template, ElementKind kind) => ElementReader.Read(template, kind);

    private static HudBlock? Template(LaidOutElement owner, string keyword) =>
        owner.Spec.SubScopes.FirstOrDefault(b => string.Equals(b.Name, keyword, StringComparison.Ordinal));

    private static HudBlock? Line(HudBlock block, string name) =>
        block.Children.LastOrDefault(c => !c.HasBody && string.Equals(c.Name, name, StringComparison.Ordinal) && c.Arguments.Count > 0);

    private static bool HasLine(HudBlock block, string name) => Line(block, name) is not null;

    private static Vector3 ColorOf(ElementSpec spec) => new Vector3(spec.Color.R, spec.Color.G, spec.Color.B) / 255f;

    /// <summary>A template's matrix as its own <c>PostReadSetup</c> leaves it, read inside <paramref name="owner"/>.</summary>
    private static Matrix4x4 TemplateMatrix(LaidOutElement owner, ElementSpec spec)
    {
        var frameW = owner.RectWidthPixels > 0 ? owner.RectWidthPixels : owner.ViewWidth;
        var frameH = owner.RectHeightPixels > 0 ? owner.RectHeightPixels : owner.ViewHeight;
        var at = new Vector3(
            EngineMath.ToPixels(spec.PositionMode, spec.Position.X, frameW, owner.ViewWidth, owner.ScreenWidth),
            EngineMath.ToPixels(spec.PositionMode, spec.Position.Y, frameH, owner.ViewHeight, owner.ScreenHeight),
            spec.Position.Z);
        return EngineMath.LocalMatrix(EngineMath.Rotation(spec.Rotation.X, spec.Rotation.Y, spec.Rotation.Z), spec.Scale, at);
    }

    /// <summary><c>SetMatrixPosRotScale</c> (P <c>00891C50</c>): the whole matrix replaced; a positive angle turns clockwise.</summary>
    private static Matrix4x4 PosRotScale(float x, float y, float angle, float sx, float sy) =>
        Matrix4x4.CreateScale(sx, sy, 1f) * Matrix4x4.CreateRotationZ(angle) * Matrix4x4.CreateTranslation(x, y, 0f);

    private static ITextFont FontFor(Context ctx, string? name) => ctx.Options.Fonts?.Invoke(name) ?? StandInFont.Instance;

    /// <summary><c>RedTextElement::GetTextHeight</c> (P <c>008C62F0</c>).</summary>
    private static int LineHeight(ITextFont font, float scaleY) => (int)(font.Height * scaleY + 0.5f);

    /// <summary>The lines <c>ClipString</c> breaks a text into at <paramref name="width"/>.</summary>
    private static List<string> Wrap(string text, ITextFont font, ElementSpec spec, int width)
    {
        var laid = TextLayout.Lay(text, font, new TextLayoutOptions
        {
            BoxWidth = width,
            ScaleX = spec.TextScale.X,
            ScaleY = spec.TextScale.Y,
            Spacing = spec.TextSpacing,
            TabSpacing = spec.TextTabSpacing,
            Break = spec.TextBreak,
        });
        return laid.Lines.Select(l => text[l.Start..Math.Min(l.End, text.Length)].TrimEnd()).ToList();
    }

    /// <summary>The last value sent to an event the element names on a line, from the simulation.</summary>
    private static HudEventValue? Sent(Context ctx, LaidOutElement e, string line) =>
        Line(e.Block, line) is { } l && EngineArgs.String(l.Arguments[0]) is { } name &&
        ctx.Options.Simulation?.Effective.TryGetValue(name, out var send) == true ? send.Value : null;

    // ---- BorderedBox -------------------------------------------------------------------------------

    /// <summary>
    /// <c>ElementBorderedBox::SetupElements</c> (P <c>005EDCE0</c>; one element on modtools, M <c>0068D900</c>, which
    /// rounds the edges): nine cells of its <c>Background</c>. The <c>Rect</c> is the middle cell and the border lies
    /// outside it, <c>Border</c> texels of the texture drawn <c>Border</c> pixels wide. Nothing draws without the texture.
    /// </summary>
    private static void BorderedBox(Context ctx, LaidOutElement box)
    {
        var texture = Line(box.Block, "Background") is { } bg ? EngineArgs.String(bg.Arguments[0]) : null;
        float bw = 0f, bh = 0f;
        if (Line(box.Block, "Border") is { Arguments.Count: >= 2 } border)
        {
            bw = EngineArgs.FloatAt(border, 0);
            bh = EngineArgs.FloatAt(border, 1);
        }
        float w = box.RectWidthPixels, h = box.RectHeightPixels;
        if (texture is not { Length: > 0 } || w <= 0f || h <= 0f || bw < 0f || bh < 0f) return;

        // A texture that is not found draws nothing in the game; the editor keeps its cells to outline.
        var (tw, th) = ctx.Options.TextureSize?.Invoke(texture) ?? (64, 64);
        if (tw <= 0 || th <= 0) return;
        float bu = bw / tw, bv = bh / th;
        static float Round(float v) => MathF.Floor(v + 0.5f);   // SetRect9, M 008398A0
        float l = Round(-bw), t = Round(-bh), r = Round(w + bw), b = Round(h + bh), rbw = Round(bw), rbh = Round(bh);
        float[] xs = { l, l + rbw, r - rbw, r }, ys = { t, t + rbh, b - rbh, b };
        float[] us = { 0f, bu, 1f - bu, 1f }, vs = { 0f, bv, 1f - bv, 1f };

        // Centre, then the sides, then the corners, as P adds them.
        foreach (var (i, j) in new[] { (1, 1), (0, 1), (2, 1), (1, 0), (1, 2), (0, 0), (2, 0), (0, 2), (2, 2) })
        {
            if (!(xs[i + 1] > xs[i]) || !(ys[j + 1] > ys[j])) continue;
            var cell = AddPiece(box, BareSpec(ElementKind.Bitmap), null, 128, Matrix4x4.Identity, Vector3.One, 1f);
            cell.Quad = cell.FullQuad = new QuadRect(xs[i], ys[j], xs[i + 1], ys[j + 1]);
            cell.Uv = new TexRect(us[i], vs[j], us[i + 1], vs[j + 1]);
            cell.Texture = texture;
        }
    }

    // ---- ObjectiveList -------------------------------------------------------------------------------

    /// <summary>
    /// <c>ElementObjectiveList::SetupText</c> (P <c>0061AE20</c>, M <c>006B9790</c>): each active (yellow) or
    /// completed (grey) objective in gamefont_tiny, wrapped 16 pixels narrower than the <c>Rect</c>, 8 pixels in
    /// and 2 apart, oldest at the top. The newest that fit are kept; the newest always shows.
    /// </summary>
    private static void ObjectiveList(Context ctx, LaidOutElement list)
    {
        var boxW = (int)MathF.Max(0f, list.RectWidthPixels - 16f);
        var maxH = MathF.Max(0f, list.RectHeightPixels - 16f);
        var font = FontFor(ctx, "gamefont_tiny");
        var objectives = CodeSamples.Objectives;
        var specs = objectives.Select(_ =>
        {
            var s = BareSpec(ElementKind.Text);
            s.Font = "gamefont_tiny";
            s.TextBreak = TextBreak.Word;
            return s;
        }).ToArray();
        var lineH = LineHeight(font, 1f);
        var heights = objectives.Select((o, i) => (float)(Wrap(o.Text, font, specs[i], boxW).Count * lineH)).ToArray();

        var shown = new bool[objectives.Count];
        var used = 0f;
        var overflow = false;
        var count = 0;
        for (var i = objectives.Count - 1; i >= 0; i--)
        {
            var next = used + heights[i] + 2f;
            if (next > maxH || overflow)
            {
                overflow = true;
                if (count == 0)
                {
                    shown[i] = true;
                    count = 1;
                }
            }
            else
            {
                used = next;
                shown[i] = true;
                count++;
            }
        }

        var y = 8f;
        for (var i = 0; i < objectives.Count; i++)
        {
            if (!shown[i]) continue;
            var color = objectives[i].Completed ? new Vector3(128, 128, 128) / 255f : new Vector3(223, 223, 32) / 255f;
            var text = AddPiece(list, specs[i], null, 128, Matrix4x4.CreateTranslation(8f, y, 0f), color, 1f);
            text.Text = objectives[i].Text;
            text.TextBoxWidth = boxW;
            text.TextBoxHeight = 1000;
            y += heights[i] + 2f;
        }
    }

    // ---- MultilineText -------------------------------------------------------------------------------

    /// <summary>
    /// <c>ElementMultilineText</c> (P <c>006083B0</c> and <c>00607180</c>): <c>NumLines</c> copies of its <c>Format</c>,
    /// each one wrapped line in a box the <c>Rect</c>'s width and one line tall. As the lines settle, with
    /// <c>AddToTop(1)</c> the oldest is at the top and the next below it; without, the newest is at the bottom.
    /// It shows what its <c>EventText</c> was sent, else sample messages.
    /// </summary>
    private static void MultilineText(Context ctx, LaidOutElement box)
    {
        if (Template(box, "Format") is not { } format) return;
        var numLines = Line(box.Block, "NumLines") is { } n ? (int)EngineArgs.FloatAt(n, 0) : 0;
        var addToTop = Line(box.Block, "AddToTop") is { } top && EngineArgs.FloatAt(top, 0) != 0f;
        var w = (int)box.RectWidthPixels;
        var h = (int)box.RectHeightPixels;
        if (numLines <= 0 || w <= 0 || h <= 0) return;

        var spec = TemplateSpec(format, ElementKind.Text);
        var font = FontFor(ctx, spec.Font);
        var lineH = LineHeight(font, spec.TextScale.Y);
        if (lineH <= 0) return;
        var maxDisplay = lineH < h ? h / lineH : 1;

        IReadOnlyList<CodeSamples.Message> messages =
            Sent(ctx, box, "EventText") is { Text: { Length: > 0 } sent } ? new[] { new CodeSamples.Message(sent) }
            : Line(box.Block, "EventText") is { } ev && EngineArgs.String(ev.Arguments[0])?.Contains("tooltip", StringComparison.OrdinalIgnoreCase) == true
                ? CodeSamples.Tooltips
                : CodeSamples.Messages;
        var formatColor = ColorOf(spec);
        var lines = new List<(string Text, Vector3 Color)>();
        foreach (var message in messages)
            foreach (var line in Wrap(message.Text, font, spec, w))
                lines.Add((line, message.Color ?? formatColor));

        var count = Math.Min(lines.Count, Math.Min(numLines, maxDisplay));
        var shown = addToTop ? lines.Take(count) : lines.Skip(lines.Count - count);
        var rotation = EngineMath.Rotation(spec.Rotation.X, spec.Rotation.Y, spec.Rotation.Z);
        var i = 0;
        foreach (var (text, color) in shown)
        {
            var y = addToTop ? i * lineH : h - lineH * (count - i);
            var local = EngineMath.LocalMatrix(rotation, spec.Scale, new Vector3(0f, y, 0f));
            var piece = AddPiece(box, spec, format, spec.ZOrder, local, color, spec.Alpha);
            piece.Text = text;
            piece.TextBoxWidth = w;
            piece.TextBoxHeight = lineH;
            i++;
        }
    }

    // ---- VehicleSeating ------------------------------------------------------------------------------

    private static readonly string[] SeatMarkers = { "Empty", "Self", "Player", "AI" };
    private static readonly string[] SeatTexts = { "EmptyText", "SelfText", "PlayerText", "AIText" };

    /// <summary>
    /// <c>ElementVehicleSeating::UpdateSeatingState</c> (P <c>0060E8C0</c>, M <c>006ABC70</c>): each seat a copy of the
    /// icon and name templates for its state. The driver's seat is at the element's origin and each next seat
    /// higher by the previous name's font height; a name is centred on its seat, and modtools draws every icon
    /// at 0.4 of its template. The element's own <c>Position</c> is taken as pixels whatever its mode, without
    /// its <c>Rect</c>'s alignment, as its <c>PostReadSetup</c> (P <c>0060E1D0</c>) calls no base.
    /// </summary>
    private static void VehicleSeating(Context ctx, LaidOutElement seating)
    {
        var own = seating.Spec;
        seating.Local = seating.LoadLocal = Matrix4x4.CreateTranslation(own.Position);

        var oy = 0f;
        foreach (var seat in CodeSamples.Seats.Take(10))
        {
            var state = Math.Clamp(seat.State, 1, 4) - 1;
            var textBlock = Template(seating, SeatTexts[state]);
            var textSpec = textBlock is null ? null : TemplateSpec(textBlock, ElementKind.Text);
            var cell = FontFor(ctx, textSpec?.Font ?? "gamefont_tiny").Height;

            if (Template(seating, SeatMarkers[state]) is { } markerBlock)
            {
                var spec = TemplateSpec(markerBlock, ElementKind.Model3D);
                var local = TemplateMatrix(seating, spec);
                local = EngineMath.ScaleOwnAxes(local, 0.4f, 0.4f);
                local.M42 += oy;
                if (spec.Mesh is { } mesh)
                {
                    var icon = AddPiece(seating, spec, markerBlock, spec.ZOrder, local, ColorOf(spec), spec.Alpha);
                    icon.Mesh = mesh;
                    icon.MeshDepth = spec.Depth ?? ModelProjection.DefaultDepth;
                }
            }
            if (textBlock is not null && textSpec is not null)
            {
                var local = TemplateMatrix(seating, textSpec);
                local.M42 += oy - cell / 2f;
                var name = AddPiece(seating, textSpec, textBlock, textSpec.ZOrder, local, ColorOf(textSpec), textSpec.Alpha);
                name.Text = seat.Name;
                if (textSpec.HasTextBox)
                {
                    name.TextBoxWidth = (int)(textSpec.TextBoxWidth * seating.ViewWidth);
                    name.TextBoxHeight = (int)(textSpec.TextBoxHeight * seating.ViewHeight);
                }
            }
            oy -= cell;
        }
    }

    // ---- Target --------------------------------------------------------------------------------------

    /// <summary>
    /// <c>Target::Update</c> (P <c>0060A360</c>): each marker a copy of its state's template, placed by code at the
    /// target's projected point in the element's <c>Rect</c> (viewport shares, the whole view by default), scaled
    /// from <c>MinScale</c> near to <c>MaxScale</c> far, an off-screen one turned to point out. The template's own
    /// <c>Position</c>, <c>Rotation</c> and <c>Scale</c> are replaced. A locked target with
    /// <c>FourSegmentLockOn(1)</c> is four brackets around it.
    /// </summary>
    private static void Target(Context ctx, LaidOutElement target)
    {
        var own = target.Spec;
        var hasRect = HasLine(target.Block, "Rect");
        var w = (hasRect ? own.RectWidth : 1f) * target.ViewWidth;
        var h = (hasRect ? own.RectHeight : 1f) * target.ViewHeight;
        var fourSegments = Line(target.Block, "FourSegmentLockOn") is { } four && EngineArgs.FloatAt(four, 0) != 0f;

        foreach (var marker in CodeSamples.Targets)
        {
            if (Template(target, marker.Template) is not { } block) continue;
            var spec = TemplateSpec(block, ElementKind.Bitmap);
            var minScale = Line(block, "MinScale") is { } mn ? EngineArgs.FloatAt(mn, 0) : 2f;
            var maxScale = Line(block, "MaxScale") is { } mx ? EngineArgs.FloatAt(mx, 0) : 0.1f;
            // LockOnManager::Target::InitCommon / InitPlayer: the distances the scale runs over.
            var (near, far) = marker.Common ? (100f, 10000f) : (100f, 500f);
            var t = Math.Clamp((marker.Distance - near) / (far - near), 0f, 1f);
            var s = (1f - t) * minScale + t * maxScale;

            var color = ColorOf(spec);
            if (marker.Friendly && Line(block, "ColorFriendly") is { Arguments.Count: >= 3 } friendly)
                color = new Vector3(EngineArgs.FloatAt(friendly, 0), EngineArgs.FloatAt(friendly, 1), EngineArgs.FloatAt(friendly, 2)) / 255f;

            var quad = BitmapQuad(spec, w, h, target.ViewWidth, target.ViewHeight, target.ScreenWidth, target.ScreenHeight);
            var locked = marker.Template.StartsWith("Locked", StringComparison.Ordinal);
            if (marker.Template is "LockedOnScreen" or "LockedOnScreenBehind")
                quad = new QuadRect(-quad.Width, -quad.Height, 0f, 0f);   // ElementTarget::Read forces Right, Bottom

            var x = (marker.Ndc.X * 0.5f + 0.5f) * w;
            var y = (marker.Ndc.Y * 0.5f + 0.5f) * h;
            var onScreen = marker.Template.Contains("OnScreen", StringComparison.Ordinal);
            if (onScreen && locked && fourSegments)
            {
                // Four brackets at the corners, each turned a quarter more and drawn 1.2 times as big.
                var dx = quad.Width * s;
                var dy = quad.Height * s;
                var corners = new[] { (x - dx, y - dy), (x + dx, y - dy), (x + dx, y + dy), (x - dx, y + dy) };
                for (var k = 0; k < 4; k++)
                    AddMarker(target, spec, block, quad, PosRotScale(corners[k].Item1, corners[k].Item2, k * MathF.PI / 2f, 1.2f, 1.2f), color);
            }
            else if (onScreen)
                AddMarker(target, spec, block, quad, PosRotScale(x, y, 0f, s, s), color);
            else
                AddMarker(target, spec, block, quad, PosRotScale(x, y, MathF.Atan2(marker.Ndc.X, -marker.Ndc.Y), s, s), color);
        }
    }

    private static void AddMarker(LaidOutElement target, ElementSpec spec, HudBlock template, QuadRect quad, Matrix4x4 local, Vector3 color)
    {
        var piece = AddPiece(target, spec, template, spec.ZOrder, local, color, spec.Alpha);
        piece.Quad = piece.FullQuad = quad;
        piece.Uv = spec.TexCoords;
        piece.Texture = spec.Texture;
    }

    // ---- Map -----------------------------------------------------------------------------------------

    private static readonly string[] MapModes = { "Small", "Large", "Spawn" };

    /// <summary>
    /// <c>ElementMap</c> (P <c>005FCFD0</c> and <c>006021F0</c>): the group at its mode's position, with the backdrop
    /// (the level's map through <c>map_mask</c>), the north pointer at its top, the player's view cone, and the
    /// command posts with their numbers and the units the code places on it, from samples. The mode is what
    /// <c>EventChangeMapMode</c> was sent, else the minimap.
    /// </summary>
    private static void Map(Context ctx, LaidOutElement map)
    {
        var mode = Sent(ctx, map, "EventChangeMapMode") is { } sent
            ? Math.Clamp(sent.Type switch { HudEventType.Int => sent.Int, HudEventType.Uint => (int)sent.Uint, _ => (int)sent.Float }, 0, 2)
            : 0;
        var suffix = MapModes[mode];
        if (mode == 0 && Line(map.Block, "AllowMiniMap") is { } allow && EngineArgs.FloatAt(allow, 0) == 0f) return;

        // SetMode: the group's translation is the mode's position; its turn and scale are as loaded, with
        // modtools' own scale for the screen's shape (M 0069B920).
        var own = map.Spec;
        var at = Line(map.Block, "Position" + suffix);
        var defaults = new[] { new Vector2(0.15f, 0.82f), new Vector2(0.5f, 0.5f), new Vector2(0.6f, 0.5f) };
        var positionMode = at is null ? RelativeMode.Viewport : at.Arguments.Count >= 4 ? EngineArgs.PositionMode(at.Arguments[3]) : RelativeMode.Pixels;
        var p = at is null ? new Vector3(defaults[mode], 0f) : new Vector3(EngineArgs.FloatAt(at, 0), EngineArgs.FloatAt(at, 1), EngineArgs.FloatAt(at, 2));
        var group = EngineMath.LocalMatrix(EngineMath.Rotation(own.Rotation.X, own.Rotation.Y, own.Rotation.Z), own.Scale,
            new Vector3(
                EngineMath.ToPixels(positionMode, p.X, map.FrameWidth, map.ViewWidth, map.ScreenWidth),
                EngineMath.ToPixels(positionMode, p.Y, map.FrameHeight, map.ViewHeight, map.ScreenHeight),
                p.Z));
        var k = ctx.Stretch;
        group = k > 1f ? EngineMath.ScaleOwnAxes(group, 1f, 1f + (k - 1f) / 4f) : EngineMath.ScaleOwnAxes(group, k, 1f);
        map.Local = map.LoadLocal = group;

        // The backdrop: a square 0.22, 0.75 or 0.65 of the view's height, unless its template has a BitmapRect.
        var backdropBlock = Template(map, "Backdrop" + suffix);
        var backdropSpec = backdropBlock is null ? BareSpec(ElementKind.BitmapMasked) : TemplateSpec(backdropBlock, ElementKind.BitmapMasked);
        var side = new[] { 0.22f, 0.75f, 0.65f }[mode] * map.ViewHeight;
        var backdropQuad = backdropSpec.HasBitmapRect
            ? BitmapQuad(backdropSpec, map.ViewWidth, map.ViewHeight, map.ViewWidth, map.ViewHeight, map.ScreenWidth, map.ScreenHeight)
            : new QuadRect(-side / 2f, -side / 2f, side / 2f, side / 2f);
        var size = new Vector2(backdropQuad.Width, backdropQuad.Height);
        var radius = mode == 0 ? CodeSamples.MiniMapRadius : CodeSamples.MapRadius;
        var backdrop = AddPiece(map, backdropSpec, backdropBlock,
            backdropBlock is not null && HasLine(backdropBlock, "ZOrder") ? backdropSpec.ZOrder : 255,
            Matrix4x4.Identity, ColorOf(backdropSpec),
            backdropBlock is not null && HasLine(backdropBlock, "Alpha") ? backdropSpec.Alpha : 0.5f, CodePieceRole.MapBackdrop);
        backdrop.Quad = backdrop.FullQuad = backdropQuad;
        // The minimap shows the player's surroundings; the others the whole map, or the part their TexCoords give.
        var window = radius / (2f * CodeSamples.MapRadius);
        backdrop.Uv = mode == 0 ? new TexRect(0.5f - window, 0.5f - window, 0.5f + window, 0.5f + window) : backdropSpec.TexCoords;
        backdrop.Mask = "map_mask";
        backdrop.MaskUv = backdropSpec.MaskTexCoords;

        // A point of the world on the map, pinned to the rim when outside it.
        Vector2 OnMap(Vector2 world, bool pin, out bool inside, out float distance)
        {
            var u = world.X * 0.5f / radius;
            var v = world.Y * 0.5f / radius;
            var m = MathF.Sqrt(u * u + v * v);
            inside = m <= 0.5f;
            distance = u * u + v * v;
            if (!inside && pin)
            {
                u *= 0.5f / m;
                v *= 0.5f / m;
                distance = 0.25f;
            }
            return new Vector2(u * size.X, v * size.Y);
        }

        // The north pointer at the top of the backdrop, 6 pixels square on its bottom edge unless its template says.
        if (Template(map, "North" + suffix) is { } northBlock)
        {
            var spec = TemplateSpec(northBlock, ElementKind.Bitmap);
            var quad = spec.HasBitmapRect
                ? BitmapQuad(spec, map.ViewWidth, map.ViewHeight, map.ViewWidth, map.ViewHeight, map.ScreenWidth, map.ScreenHeight)
                : new QuadRect(-3f, -6f, 3f, 0f);
            var north = AddPiece(map, spec, northBlock, HasLine(northBlock, "ZOrder") ? spec.ZOrder : 127,
                PosRotScale(0f, -size.Y / 2f, 0f, spec.Scale.X, spec.Scale.Y), ColorOf(spec), spec.Alpha);
            north.Quad = north.FullQuad = quad;
            north.Uv = spec.TexCoords;
            north.Texture = spec.Texture;
        }

        // The view cone: the model lies in its x-z plane, z down the map; turned to point up.
        if (Template(map, "PlayerFOV" + suffix) is { } fovBlock && TemplateSpec(fovBlock, ElementKind.Model3D) is { Mesh: { } fovMesh } fov)
        {
            var s = fov.Scale.X;
            var a = -MathF.PI;
            var local = new Matrix4x4(
                s * MathF.Cos(a), s * MathF.Sin(a), 0f, 0f,
                0f, 0f, s, 0f,
                -s * MathF.Sin(a), s * MathF.Cos(a), 0f, 0f,
                0f, 0f, 0f, 1f);
            var cone = AddPiece(map, fov, fovBlock, HasLine(fovBlock, "ZOrder") ? fov.ZOrder : 123, local, ColorOf(fov), fov.Alpha);
            cone.Mesh = fovMesh;
            cone.MeshDepth = fov.Depth ?? ModelProjection.DefaultDepth;
        }

        // Command posts, pinned to the rim, 6-pixel icons at the post's MapScale (2.5 on the stock posts).
        var postBlock = Template(map, "Post" + suffix);
        var postText = Template(map, "PostText" + suffix);
        var numbers = !(Line(map.Block, "PostNumbers") is { } pn && EngineArgs.FloatAt(pn, 0) == 0f);
        foreach (var post in CodeSamples.Posts)
        {
            var point = OnMap(post.World, pin: true, out _, out var d2);
            if (postBlock is not null)
            {
                var spec = TemplateSpec(postBlock, ElementKind.Bitmap);
                var icon = AddPiece(map, spec, postBlock, HasLine(postBlock, "ZOrder") ? spec.ZOrder : 128,
                    PosRotScale(point.X, point.Y, 0f, 2.5f, 2.5f), post.Color, spec.Alpha);
                icon.Quad = icon.FullQuad = spec.HasBitmapRect
                    ? BitmapQuad(spec, map.ViewWidth, map.ViewHeight, map.ViewWidth, map.ViewHeight, map.ScreenWidth, map.ScreenHeight)
                    : new QuadRect(-3f, -3f, 3f, 3f);
                icon.Uv = spec.TexCoords;
                icon.Texture = spec.Texture ?? "hud_flag_icon";
            }
            if (numbers && postText is not null)
            {
                var spec = TemplateSpec(postText, ElementKind.Text);
                spec.Font ??= "gamefont_small";
                // Five pixels above the post; on the minimap fading out toward the rim.
                var alpha = spec.Alpha * (mode == 0 ? Math.Clamp(1f - 4f * d2, 0f, 1f) : 1f);
                var number = AddPiece(map, spec, postText, HasLine(postText, "ZOrder") ? spec.ZOrder : 127,
                    Matrix4x4.CreateTranslation(point.X, point.Y - 5f, 0f), ColorOf(spec), alpha);
                number.Text = post.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        // Units inside the map: friends always, enemies while they fire, in the firing colour (white).
        if (Template(map, "Target" + suffix) is { } unitBlock)
        {
            foreach (var unit in CodeSamples.Units)
            {
                var point = OnMap(unit.World, pin: false, out var inside, out _);
                if (!inside || (!unit.Friendly && !unit.Firing)) continue;
                var spec = TemplateSpec(unitBlock, ElementKind.Bitmap);
                var color = unit.Firing ? Vector3.One : unit.Friendly ? CodeSamples.Friend : CodeSamples.Enemy;
                var icon = AddPiece(map, spec, unitBlock, HasLine(unitBlock, "ZOrder") ? spec.ZOrder : 126,
                    PosRotScale(point.X, point.Y, unit.Heading * MathF.PI / 180f, 1f, 1f), color, spec.Alpha);
                icon.Quad = icon.FullQuad = spec.HasBitmapRect
                    ? BitmapQuad(spec, map.ViewWidth, map.ViewHeight, map.ViewWidth, map.ViewHeight, map.ScreenWidth, map.ScreenHeight)
                    : new QuadRect(-3f, -3f, 3f, 3f);
                icon.Uv = spec.TexCoords;
                icon.Texture = spec.Texture ?? "troop_icon";
            }
        }
    }

    /// <summary>A font for when none is given: every character 6 pixels wide in a 12-pixel cell.</summary>
    private sealed class StandInFont : ITextFont
    {
        public static readonly StandInFont Instance = new();
        public int Height => 12;
        public int Ascent => 10;
        public FontGlyph? Find(char c) => c == ' ' ? new FontGlyph(c, 4, 0, 0, 0, 0) : new FontGlyph(c, 6, 0, 1, 5, 11);
    }
}
