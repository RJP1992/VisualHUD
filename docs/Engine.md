# How the game lays out and draws a .hud file

What `SwbfHudEditor.Core/Layout` implements, with where each rule was read. Addresses are
the modtools executable `BF2_modtools_MemExt.exe` (**M**) or the Phantom build with names
(**P**); retail Steam and GOG match modtools in everything here. The deeper research is
in BF2GameExt's `docs/RE/HUDSystem.md`, `HUDElementProperties.md` and `HUDAuthoring.md`.

Matrices are row-vector, as `PblMatrix` and D3DX: a point is `p × M`, and `A × B` applies
A first. Floats are single precision, as in the game.

## Three spaces

- **HUD pixels.** What `.hud` numbers become once read: x right, y down, one unit a
  device pixel of the screen (`HUD::Element::SetupViewportDimensions`, P `005F57D0`).
- **Interface units.** The plane the HUD screen groups are drawn on, x right, y up, 640
  units across the screen whatever its width. The interface camera
  (`RedInterfaceScreen::Render`, M `00817870`) has frustum width `t = 2 tan 30°` at
  distance 1 and height `t·H/W`, and the plane sits at `z = -640/t`.
- **Screen pixels.** `x = (u + 320)·W/640`, `y = H/2 - v·W/640`.

The screen is `RedInterfaceScreen::s_screenFull`, the back buffer
(`RedRenderer::internalResetState`, M `0080CD80`), which also sets
`m_screenAspectRatio = H/W`.

## Reading a line

Each element type's `ReadData` takes its own keys and hands the rest down to
`HUD::Element::ReadData`. A later line replaces an earlier one.

- Names go through `HUD::TranslateString` (P `00618150`): the first table name the text
  **starts with**, ignoring case. `"ScreenSpace"` reads as Screen; `"Viewpor"` as nothing.
- A quoted string is told from a number by the argument's raw value; an unquoted word is
  its hash, so it reads as `true` and never as a name.
- `Position(x, y, z [, mode])` (P `005F4540`): with four arguments, one of `Pixels`,
  `Screen`, `Viewport`, `Frame`, or a bool where true is Viewport. **Without a mode, or
  with an unknown name, Pixels.**
- `Rotation(x, y, z)` in degrees, each wrapped into [0, 360); the matrix is
  `RotationX × RotationY × RotationZ` (P `005F51E0`).
- `Scale(x, y, z)`, applied after the rotation: `R × S` (`HUD::Matrix::ApplyScale`,
  P `005F2C40`), so it stretches along the parent's axes.
- `Rect(w, h [, h [, v [, mode]]])` on group types (`ElementGroupBase::ReadData`,
  P `005FC510`): 1 × 1, Viewport, Left/Top by default. Mode is one of the first three
  names or a bool (0 is Pixels); an unknown name keeps the mode.
- `BitmapRect(w, h [, h [, v [, mode]]])` (P `005FA670`): the mode is read only with
  exactly five arguments: one of the first three names, else a number where **0 means
  Screen**, else Viewport.
- `TextAlignment(h, v)`: `Left`, `Right`, `Center` (Right before Center) and `Top`,
  `Bottom`, `Center`, `Baseline`; a half that names nothing keeps its value.
- `ZOrder` starts at **128** (`RedInterfaceElement` constructor, P `00891350`).
- `Viewport(n)` counts only on a top-level element: `HUD::Element::GetViewport`
  (P `005F3BE0`) asks the element's group, up to the top.

## Where a block can go

What the Insert menus offer where (`HudBlockTemplates.WhyNot`):

- **The top of a file.** `HUD::Manager::Load` makes an item of every block, by the hash of
  its keyword: an element, a transform, a `FileInfo` or a `ViewPort`.
- **A `ViewPort`.** `ViewPort::Read` (P `0061EDF0`) reads its scope once per camera and
  makes an item of every block in it, elements included.
- **A group, and every element built on one.** `ElementGroupBase::Read` (P `005FC310`) hands
  each block to `ReadChild` (P `005FC3F0`), which makes the item and keeps it as a child only
  when its factory makes an element; a transform there is made but belongs to no group. Map
  (P `006009E0`), Target (P `006098C0`), MultilineText (P `00607DA0`) and VehicleSeating
  (P `0060E300`) first match their own templates, by the hash of the line's keyword, so a
  template's quoted name is a label, then hand any other block to `ReadChild` too.
- **Any other element** reads with `HUD::Item::Read` (P `00617D10`): a block in its body is
  read as more lines of the element itself, so a Bitmap inside a Text makes nothing.

The editor keeps transforms to the top of a file and to ViewPorts, where `hudtransforms.hud`
has them and where the event simulation reads them. A `FileInfo` goes first: the game reads it
to decide whether to load the file at all.

## Setup: what each element's PostReadSetup leaves

`HUD::ConvertRelativeToPixels(mode, value, frame, view, screen)` (M `00691120`):
Pixels keeps the number, Screen multiplies by the screen, Viewport by the view, Frame by
the frame. For an element:

- **view** is `GetContainerViewWidth/Height`: the screen with one viewport.
- **frame** is `GetContainerFrameWidth/Height` (P `005F3630`): the element's group's Rect
  in pixels, else the view. A child's group is set by `ElementGroupBase::ReadChild`
  (P `005FC3F0`) before the child is read.

Then:

1. **Every element** (`HUD::Element::PostReadSetup`, P `005F3CA0`): the position
   converted, z kept, rotation times scale. Modtools also passes x through
   `ConvertXForWideScreen` (M `00691210`), which is the identity on shipped builds.
2. **Groups** (`ElementGroupBase::PostReadSetup`, P `005FC260`, through `SetAlignment`,
   P `005FC7E0`): the origin moves by `-{0, ½, 1}` of the Rect, so a child at (0, 0) sits
   at the Rect's top left.
3. **Bitmaps** (`ElementBitmapBase::PostReadSetup`, M `00698790`): `BitmapRect` makes a
   quad around the position (Left `0..w`, Center `-w/2..w/2`, Right `-w..0`), its corners
   converted in the rect's mode, each edge rounded with `floor(v + 0.5)`
   (`RedBitmapElement::SetRect`, M `00839130`). A bitmap without `BitmapRect` draws
   nothing; its texture never sizes it. Then `RedInterfaceElement::Scale(1, 0.75 / (H/W))`
   (M `008285F0`) stretches the bitmap's own y axis: 4/3 at 16:9.
4. **Bars** (`ElementBarBitmap::PostReadSetup`, P `005F7480`): the bar base's setup calls
   `SetValue(Value)` before the bar has stored its width and U span (constructor values 1
   and 1). `SetValue` (P `005F7920`) fills only when the value as read differs from the
   fraction by over 1e-4, so with the stock 0..1 range a bar loads **full**, and with any
   other range the first fill collapses it to a pixel. The fill keeps the left edge and the
   left U: `right = left + f·width`, `u1 = u0 + f·u1` (the U span is wrong unless
   `u0 = 0`). Defaults: Value 1, MinValue 0, MaxValue 1, ScaleTexture and ScaleSize on.
5. **Segmented bars** (`ElementBarSegmented::SetValue`, M `00696AB0`): each lit segment's
   matrix is replaced. In a row, x from 0 in steps of `(int)RectWidth / N`. On the ellipse,
   from `AngleStart` in steps of `|AngleEnd - AngleStart| / N`: `x = rx + rx·sin a`,
   `y = ry - k·ry·cos a` with `k = 0.75 / (H/W)`, rotated by a. The template `Segment` gives
   the quad; its own position and the bitmap stretch are dropped.
6. **Text** (`ElementText::ReadData`, P `0060C850`): `TextBox` is
   `(int)(w × view width), (int)(h × view height)`. `RedTextElement::WriteCache`
   (P `008C6A20`) lays lines out from the element's origin: x 0, `box - line` or half that;
   y 0, `box - lines`, half that, or the font's ascent up for Baseline.
7. **Models** (`ElementModel3D::PostReadSetup`, P `00605BA0`): each `MeshInfo`'s
   position is converted with the element's sizes, and when that mesh is shown
   `ElementModel3D::SetMesh` (P `006063A0`) replaces the element's whole matrix with the
   MeshInfo's (identity rotation and scale 1 unless it says otherwise).

## Draw

- **Screen group.** `HUD::Manager::Open` (M `006B8A00`) makes the HUD's groups with the
  safe zone off and the 640 × 480 mapping on. `RedInterfaceScreen::Render` draws a group
  under `diag(640/W, -480/H, 1)` with translation `(-320, 240, -640/t)`.
- **Letterbox.** The loader (M `006B7300`) sets each group's local matrix to
  `SetMatrixPosScale(0, d/2, 1, 1 - d/H)` with `d = 0.75W - H`, when the screen is at
  least 2 pixels wider than 4:3. With the mapping that leaves y at
  `H/2 + a(2 - a)(y - H/2)` with `a = 0.75 W/H`: 40 to 680 at 1280×720. On a screen
  narrower than 4:3 there is no letterbox and the factor is `a`.
- **Each element** (draw, M `00816FA0`): `world = local × parent`, translation x and y
  rounded to whole interface units with `floor(v + 0.5)`; children start from the rounded
  matrix. GameExt's `HudSubPixel` (on by default) skips the rounding.
- **Colour and alpha.** A child's colour is always multiplied by its group's RGB; its
  alpha by the group's only with `PropagateAlpha(1)` (`RedGroupElement::RenderUsingContext`,
  P `008C38F0`).
- **Order.** `RedGroupElement::AddElement` (P `008C3650`) puts a new child after every
  child with an equal or higher z-order; a `ZOrder` line (`SetZOrder`, P `00891EE0`) moves
  it in front of the first with an equal or lower one. The list runs from high to low and
  is drawn from its head: a lower ZOrder draws on top.
- **Runtime HUD scale.** `HUD::Element::SetRuntimeScale` (P `005F5400`) rescales the axes
  of `Scalable(1)` elements and keeps their translation.

## Models

`SwbfHudEditor.Core/Layout/ModelProjection.cs` ports how a `Model3D` is drawn, and
`SwbfHudEditor.App/Views/MeshRenderer.cs` draws the mesh with it.

- **The camera.** The HUD's models go through the same camera as the rest of the HUD: it
  looks down -z with a horizontal field of 60° (`t = 2 tan 30° = 1.1547`), near plane 1.0,
  far 10000, and the same focal length `W / t` on both axes (`RedInterfaceScreen::Render`,
  M `00817870`; `internalUpdateSceneConstants`, M `0080ACB0`). It is a true perspective.
- **LocalToWorld.** `Red3DModelElementLite::RenderUsingContext` (M `00839F80`) takes the
  element's world matrix, then `LocalToWorld` (M `00839D80`) turns its up row over and
  multiplies only the translation by `Depth × 640 / W` (`GetDepthMultiplier`, M `00839D60`,
  while the HUD draws). That pulls the model from the HUD's plane toward the camera.
- **Size.** In x the two `640 / W` factors cancel: one mesh unit is `W / (640 × Depth)` HUD
  pixels in the element's own frame (300 at 1920 wide with the default depth 0.01), with the
  mesh's y up. In y the mesh gets the same vertical mapping as everything else: the 8/9 band
  at 16:9 on the stock layout, square under `AuthoredRatio`. The element's and its groups'
  `Scale` and `Rotation` apply as to any child. Mesh units are used raw, with no fitting.
- **Perspective.** The mesh's z is never scaled by `640 / W`, so a vertex `dz` nearer the
  camera is drawn `Z0 / (Z0 - dz)` times as far from the screen's middle, where
  `Z0 = Depth × 640/W × (640/t - z)`: 1.85 at 1920 wide. Anything nearer than the near plane
  is cut off. The higher the resolution, the stronger the perspective.
- **Nodes.** Each segment is drawn through its node's default pose (`RedModel::Render`,
  P `008993D0`): the `.msh` `TRAN` of its `MODL`, chained up the parents. Hidden nodes and
  shadow volumes are left out.
- **Mesh and MeshInfo.** `ElementModel3D::SetMesh` (P `006063A0`) runs on the `Mesh` line
  and on each mesh an event sends. It looks the mesh up in the MeshInfo table: a `MeshInfo`
  for it replaces the element's whole matrix and its depth (0.01 unless it has a `Depth`);
  without one the element's own matrix is put back with depth 0.01. The table is read after
  the element's other lines (`ElementModel3D::Read`, P `00605C70`), so the mesh of a `Mesh`
  line keeps the element's own matrix, and a `MeshInfo` only places a mesh an event sends.
  An element's `Depth` line writes the drawable directly and lasts until the mesh changes.
  If the model is already loaded when the `Mesh` line is read, `SetMesh` also resets the
  `Position`, `Rotation` and `Depth` lines written before it; the stock files write `Mesh`
  first. The editor does not model that reset, because it depends on what is loaded first.
- **InheritMeshInfo.** `InheritMeshInfo("name")` shares the table of a Model3D made before it
  (P `00606150`). Each model that reads a table converts its positions in place as it
  loads (`ElementModel3D::PostReadSetup`, P `00605BA0`; `Matrix::ApplyRelativeMode`,
  P `005F2AC0`). A shared table is therefore converted once per model that reads it: a
  `"Viewport"` position is multiplied by the view's size twice with one heir. Only
  `"Pixels"` positions come through as written. The editor draws it the same way.
- **Hardpoint** (`F × bone` before `LocalToWorld`) and the model's lighting are not
  modelled: models are drawn unlit, textured and coloured by their vertices, nearer
  surfaces over farther ones.

## Text

`SwbfHudEditor.Core/Layout/TextLayout.cs` ports the game's text layout; the font comes from its
FontEdit `.fff` file (`SwbfHudEditor.Core/Assets/FffFont.cs`): `BORK`, the glyph count (u16), the
cell height (u8) and two bytes always 0; per glyph its character (u16), advance, left, top, right
and bottom, the fields of the munged `Glyph`; then each glyph's image in that order, its width and
height (u8 each, the ink box's) and a byte of coverage a pixel, rows top to bottom, to the end of
the file. The layout and the coverage were read from FontEdit and FontMunge and checked against
FontMunge's textures: FontMunge packs the coverage into four bits of alpha (the byte divided by
17), so the faintest level a file has, 16, draws as nothing in game.

- **Line height** (`GetTextHeight`, P `008C62F0`): the font's cell height times `TextScale` y,
  rounded, plus a leading no `.hud` line sets. 17 pixels for `gamefont_small`.
- **Breaking** (`ClipString`, P `008C55B0`). Characters move by their rounded advance plus
  `TextCharacterSpacing` (1 by default); a tab to the next stop, `TextTabSpacing` or eight spaces.
  A line ends at a line break. With `TextBreak("Word")` and a box, a line that would pass the
  box's right edge ends at its last run of spaces, or before the character if it has none.
  `"No Reformat"` does not wrap but counts a run of spaces, tabs and line breaks as one space,
  when breaking and aligning only: the draw still moves past every one. `TranslateString` takes
  the first name a value starts with, so `"Word Reformat"` reads as `"Word"`: a file cannot ask
  for both.
- **Lines in the box** (`GetMaxLines`, P `008C5F90`): with a box height, as many lines as fit,
  at least one; without, 80. Text past them is dropped, except with `Bottom` alignment, which
  keeps the last lines, as a scrolling log would.
- **Placing** (`WriteCache`, P `008C6A20`): the first line by the vertical alignment (Top 0,
  Bottom and Center inside the box, Baseline the font's ascent above the origin), each line by
  the horizontal one inside the box (with no box, Right ends at the origin and Center straddles
  it). A printing character (one with pixels, `IsPrintingChar`, P `008C82F0`) that would pass the
  box's right edge is not drawn, nor is the rest of its line: text is clipped, never squeezed.
- **Strings.** A `Text` key the localization lacks is shown as the key itself, with a warning in
  the log (`ElementText::ReadData`, P `0060C978`). A `{name}` in a string becomes the button
  bound to that action (`FindReplaceMagicStrings_`).

The preview draws each character where the layout places it: the glyph's image at its ink box
in the cell, filled with the text's colour at the game's coverage. A font that cannot be found is
stood in for with a system typeface at the stock size of its name.
Until its events send it something, a text element shows an example of what they would send
(Text: Examples or Long in the toolbar) or the keys and event names (Keys); with the game's showing
and hiding on, a text that shows only what its events send stays empty until then.

## Several files

A HUD is several `.hud` files: the stock `ingame.lvl` loads `hudtransforms` and the HUD files,
and a mod can load more lvls with their own (BF3's `Platform.lua` picks a HUD lvl and a minimap
lvl). Every file loads into the same screen groups:

- **Each file is read on its own**, with its own `FileInfo`. Under GameExt a file with
  `AuthoredRatio` or `TrueWidescreen` is laid out on its ratio, while a file without one keeps
  the stock layout beside it.
- **Events and transforms are global**: a transform in one file feeds elements in another,
  and an event reaches every file's elements.
- **Top-level order.** A top-level element's drawable joins its screen group when the element is
  enabled (`HUD::Element::UpdateEnableState`, P `005F5C00`), after every drawable of an equal or
  higher z-order, its `ZOrder` line having set its z-order before. Among top-level elements of
  equal z-order, the one enabled later draws on top, whatever its file. Unless the events decide
  what shows, the editor uses load order, file by file.

## GameExt

The **GameExt** target adds what BF2GameExt does; the C# is a line-for-line port of its
`hud_true_widescreen_core.hpp` and `hud_bar_fill_from_core.hpp`, and the tests run its
C++ test vectors.

- **AuthoredRatio(w, h) / TrueWidescreen(1)** in `FileInfo`, with one viewport: the
  file is laid out on a screen of its ratio R at the real height, `WL = R·H` (a 4:3 file
  only on a screen wider than 4:3). Screen and Viewport widths are WL; bitmaps, rings and
  the minimap get no stretch; each top-level piece is drawn under its own parent, one pixel
  to one pixel, slid by `{0, ½, 1}·(W - WL)` by the third of WL its x is in, or by its
  `ScreenAnchor` share. A top-level element at (0, 0) without an anchor is a plain
  container whose children are the pieces; a `Target` is never slid.
- **FillFrom("Right" | "Bottom" | "Top")** on bars.
- **No rounding** by default (`HudSubPixel`).

The **Vanilla** target ignores all of these lines, as the stock game does (it logs an
error for each).

## Events

`SwbfHudEditor.Core/Events` simulates what the game's events do, for the Events tab and
`--preset`/`--send`. The editor keeps the latest send of each event and the order sends came in.
The events always set what their handlers set; whether they also decide what shows is a choice
(`LayoutOptions.ShowByEvents`, the Events tab's *Show and hide as the game does*, `--show`):
without it every element shows, as before any event.

- **Showing and hiding.** An element starts disabled (`HUD::Element` constructor, P
  `005F1E70`, sets `enableState_Disabled`). `EventEnable` and `EventDisable` take any event,
  whatever its value (P `005F3410`), and each binds one handler, the last line's. So an element
  is enabled when its enable event was sent after its disable event, and drawn when it and every
  group above it are enabled. Every one of the stock HUD's 184 elements has an `EventEnable`;
  an element without one never shows in the game.
- **Transforms** run in declaration order, each when one of its triggers has been sent: the
  native classes as the game's code (`TransformNumberColor::CalculateOutputColor`
  P `0061D1D0`, `TransformNumberType::CalculateInputValue` P `0061DE70` with its odd wrap,
  `CalculateOutputVector3` P `0061E260`, `TransformNumberColorBlend::EventInput` P `0061D940`
  with Alpha mode and a weight of 127 by default, `TransformNameMesh::EventInput` P `0061C8E0`
  by name hash), and GameExt's Math, Lerp and Compare as its docs describe. Names inside a
  `ViewPort` with `EventNameFilter("player%")` are renamed to player 1.
- **Handlers.** `EventValue` fills a bar (numbers only); `EventText` sets a String or looks a
  Uint up as a localization hash; `EventNumber` prints with `IntegerFormat` (`%d`),
  `FloatFormat` (`%f`), `Percent` (`%.0f%`), `NumberToTime` or `InfiniteDashes`
  (`ElementText::EventNumber`, P `0060BE50`); `EventColor` replaces the colour (Color only);
  `EventBitmap` and `EventMesh` swap the texture or model by name; a group's
  `EventPosition` is read in its Rect mode and aligned again, `EventScale` sets its own axes'
  lengths and `EventRotation` rebuilds its rotation keeping them (P `005FB930`, `005FBDE0`,
  `005FBAC0`); `EventAlpha` replaces a group's alpha.
- **One point per position event.** Every group bound to an `EventPosition` event is moved to the
  point it sends, read in each group's Rect mode and aligned by its Rect; a group's written
  `Position` only lasts until then. Until such an event is sent, the editor takes it to have sent
  the first bound group's written position, so groups that follow it together stay together;
  not while the events decide what shows, where a group the event has not reached is where it is
  written, as in the game.
- **GameExt's world-tracking positions** (reticule, lock-on, target, command posts) in a file it
  lays out land on the real screen point: x is handed over as `(v·W − slide) / WL`.
- **Names** hash with `PblHash` (FNV-1a, each byte OR 0x20), so they match whatever their case.

## Defaults

Saving leaves out a property line that sets what the element's constructors already set
(`SwbfHudEditor.Core/Layout/ElementDefaults.cs`), when the line is in its plain form and its
key appears once in the block:

| Element | Lines left out | Read from |
|---|---|---|
| Every element | `Position(0, 0, 0[, mode])`, `Rotation(0, 0, 0)`, `Scale(1, 1, 1)`, `Alpha(1)`, `Color(255, 255, 255)`, `ColorChange(255, 255, 255)`, `ColorChangeRate(0.3)`, `ColorPulseRate` and `FadeHoldTime` at `FLT_MAX`, `FadeInTime(0)`, `FadeSustainTime(0)`, `FadeOutTime(0)`, `Viewport(0)`, `UseChangeColor(0)`, `EditOnly(0)`, `Scalable(0)` | `HUD::Element` constructor P `005F1E70` (flag bits 0, 3 and 5 cleared), `Envelope` P `0060EDC0` |
| Group | `Rect(1, 1[, "Left"[, "Top"[, "Viewport"]]])`, `PropagateAlpha(0)` | P `005FB4D0` |
| Bitmaps | `TexCoords(0, 0, 1, 1)`, `BitmapStyle("Normal")`, `BlendMode("Alpha")` | `RedBitmapElement` constructor P `008C8A10` |
| Bars | `Value(1)`, `MinValue(0)`, `MaxValue(1)`; BarBitmap `ScaleTexture(1)`, `ScaleSize(1)`, `FlashyScale(0.2)`, `FlashyIncFadeOutTime(0)`, `FlashyDecFadeOutTime(0)`, GameExt's `FillFrom("Left")`; BarSegmented `NumSegments(0)`, `Circular(1)`, `AngleStart(0)`, `AngleEnd(0)` | `ElementBar` and its subclasses' constructors |
| Text | `TextScale(1, 1)`, `TextAlignment("Left", "Top")`, `TextBox(0, 0)`, `TextCharacterSpacing(1)`, `TextTabSpacing(0)`, `IntegerFormat("%d")`, `FloatFormat("%f")` | `RedTextElement` constructor P `008C4E90` |
| Model3D | `Depth(0.01)`, `Lighting(1)`; in a `MeshInfo`, `Position(0, 0, 0)`, `Rotation(0, 0, 0)`, `Scale(1, 1, 1)` | P `008DECE0`, `MeshInfo::Read` P `00605FC0` |

Kept whatever their value: `ZOrder` (a child's line moves it in front of its equals even at
128), every line of a key written more than once in a block, the lines that make the element,
events, a `MeshInfo`'s `Depth` (it replaces the element's), a word where a number goes (the game
reads its hash), keys whose default is not known (`TextStyle`, `TextBreak`, `Percent`, ...), and
everything outside the elements the game builds as written: `FileInfo`, transforms, `ViewPort`
settings, sounds, `Segment` templates, and Map, Target, VehicleSeating, ObjectiveList,
BorderedBox and MultilineText with all they hold.

## Not modelled yet

- **Map, Target, MultilineText, ObjectiveList, VehicleSeating, BorderedBox** draw from
  code the preview does not run; they show as outlines of their Rect.
- **Time.** The simulation has no clock: fades (`FadeInTime`, `FadeHoldTime` and the rest) are
  at their end, a Lerp has reached its input, a Compare's hysteresis is not applied, and
  `EventChanged`, `ColorChange` flashes and pulse rates are not shown.
- **Code-driven elements.** What the Map, Target, VehicleSeating, ObjectiveList and
  MultilineText elements draw from the game's state.
- **ReticleCorrection.** GameExt shifts `player1.weaponN.reticule.position`'s y for stock files
  on a wide screen; the editor sends the value as typed.
- **Perspective for flat elements.** Depth is dropped for bitmaps and text: one tilted out
  of the screen plane by `Rotation` x or y is drawn foreshortened but without perspective.
  Models are drawn in perspective (see [Models](#models)).
- **ProceduralBarBitmap**'s flicker and its 1.3× size.
