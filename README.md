# VisualHUD

An editor for Star Wars Battlefront II (2005) `.hud` files that draws a HUD the way the game
does, on the screen you pick, for the stock game or for the game with
[BF2GameExt](https://github.com/S1thK3nny/BF2GameExt).

The layout follows the engine's own code rather than estimates: positions, Rects and
alignment, the 640 × 480 mapping and the letterbox a wide screen gets, the bitmap stretch,
bar fills, segmented rings, draw order and rounding. How each rule was read is in
[docs/Engine.md](docs/Engine.md), along with what is not modelled yet.

## Build and run

The release is one file, `VisualHUD.exe`, that runs on 64-bit Windows 10 or 11 with nothing
else installed. To build it, run `tools\publish.bat`; it writes `dist\VisualHUD.exe`.

To work on the editor you need the .NET 8 SDK (or newer) on Windows.

```bash
dotnet build SwbfHudEditor.sln
```

Then run `VisualHUD.bat`, or `src/SwbfHudEditor.App/bin/Debug/net8.0-windows/VisualHUD.exe`
with one or more `.hud` files as arguments. Textures, models, fonts, the transforms files and
the English localization are looked up next to the files and in `C:\BF2_ModTools\data\Common`,
then in any asset folders you add (see [Assets](#assets)).

```bash
dotnet test SwbfHudEditor.sln
```

## A HUD's files

A HUD is usually several `.hud` files: the game loads the stock transforms and HUD files, and
a mod can load more. They load additively, into the same screen groups, and each is laid out
by its own `FileInfo`. The editor works on such a set:

- **File > New HUD file** (Ctrl+N) writes a new file with its `FileInfo` and one viewport, in
  the folder you pick, laid out the stock way, with `TrueWidescreen`, or with
  `AuthoredRatio(w, h)`. It opens on its own, or is added to the HUD already open.
- **File > Open** takes one file or several, loaded in name order; **Add HUD file** loads
  more after them; **Remove file from HUD** takes the selection's file out (the file on disk
  is not changed). Several files on the command line load in that order.
- The tree has a node per file, in load order. The transform files the HUD needs load first,
  marked *loaded with the set*: the folder's `hudtransforms.hud` (else the stock one) and its
  other `hudtransforms*.hud`, such as GameExt's.
- Under GameExt, a file with `AuthoredRatio` or `TrueWidescreen` is laid out on its ratio
  while the files without one keep the stock layout, side by side. The tree notes the files
  GameExt lays out; the guides and the layout line are for the selection's file.
- Events and transforms reach every file. Each file's checkbox shows or hides its elements.
- **Save** writes every file with unsaved changes, and only those; **Save file as** writes
  the selection's file under a new name. A saved file keeps its comments, blank lines,
  spacing, indentation and line endings: only the lines you edited change.
- Saving leaves out the lines that only restate the engine's defaults, such as `Alpha(1)`
  or `Rotation(0, 0, 0)`; the Properties panel shows them in grey italics. Lines the game
  needs whatever their value stay: `ZOrder`, a key written twice in a block, anything whose
  default is not known, and any line with a comment before or after it. The list is in
  [docs/Engine.md](docs/Engine.md#defaults).

## Assets

Textures, meshes, fonts and the English localization are looked up in the HUD's own folders
(the `hud` folder and its `Common` folder's `interface`, `effects`, `mshs`, `fonts` and
`Localize`), then in `C:\BF2_ModTools\data\Common`, then in the asset folders.

The strings are the ones the game ends up with: the stock `Localize\english.cfg`, then the PC
build's `Localize\PC\English.cfg` over it, then the mod's own two the same way (for a file in
`data_BF3\Common\hud`, `data_BF3\Common\Localize`), a later file's string replacing an earlier
one's for the same key.

- **Searched automatically:** the data folder of the mod each open file belongs to, with all
  its subfolders (all of `data_BF3`, its `Sides` and `Load` included, for a file in
  `data_BF3\Common\hud`), then the stock `C:\BF2_ModTools\data`. This needs the mod tools'
  `<data>\Common\hud` layout; a file elsewhere gets no automatic folder.
- **File > Add asset folder** adds more folders, searched the same way after those, for
  whatever none of them has. A name found in several places resolves to the first path in
  alphabetical order.
- Added folders are remembered in `%LOCALAPPDATA%\VisualHUD\settings.json`. Every folder is
  read in the background, once per session. A large folder (all of `C:\BF2_ModTools` is about
  450,000 files) takes some seconds.
- The **Assets** tab lists what the open files name, where each was found, or that it is
  missing, and what uses it:
  - textures: `Bitmap` and `Mask` lines, and the textures of the models found;
  - meshes: `Mesh`, `MeshInfo`, and the transforms' `NameMesh` rows;
  - fonts: `TextFont`, a `.fff` file;
  - strings: the `Text` keys, missing or with the file their string comes from;
  - the localization files, in the order they lie over each other.
- **Rescan** reads the folders again after files were added to them; the × removes an added
  folder.
- Textures are `.tga` files and models `.msh` files, as the game's munge reads them; a `.png`
  does not count. A font, its `.fff` file, gives text its widths and its glyphs.

## The canvas

- **Game**: *GameExt* honours `AuthoredRatio`, `TrueWidescreen`, `ScreenAnchor` and
  `FillFrom`; *Vanilla* ignores them, as the stock game does, and stretches a HUD on a
  wide screen.
- **Screen**: the resolution the game draws at. With Shader Patch's display scaling that is
  the resolution the game sees (1280×720 on a 4K screen at 150%), and it decides how big a
  `"Pixels"` value is.
- **View**: whose screen group to show.
- **Snap**: the stock draw snaps every element to 1/640 of the width; GameExt's
  `HudSubPixel` (on by default) does not. Switching game resets it.
- **Outlines** marks what draws nothing here: a missing texture, a bitmap without a
  `BitmapRect`, the box of an element the game draws in code.
- **Guides**: the band the stock game squashes the HUD into on a wide screen, or, for a
  file GameExt lays out, the thirds of its layout and where each lands.
- **Hidden** draws faintly what the game's showing and hiding keeps hidden (see
  [Events](#events)).
- The ⚙ button holds **HUD scale**, which resizes `Scalable(1)` elements as the game's video
  option does.
- The elements the game draws in code are drawn as their code lays them out: a BorderedBox's
  frame, an ObjectiveList's objectives, a MultilineText's lines, a VehicleSeating's seats, a
  Target's markers and the Map's backdrop, north pointer, view cone, posts and units, with
  sample objectives, messages, seats, targets, posts and units. A sent `EventChangeMapMode`
  shows the large or spawn map; a sent `EventText` shows in a MultilineText.
- Bitmaps and text blend as the game blends them: `BlendMode("Additive")` adds to what is under
  it, a group's `BlendMode` reaches the children it holds when the line is read, and the Shadow
  style draws the game's black copy at half the alpha, W/554 pixels right and H/554 down.
- Models are drawn as the game draws them, through its camera: one mesh unit is
  `W / (640 × Depth)` pixels across (300 at 1920 wide), a nearer part is larger, and a
  `MeshInfo` places only a mesh its event sends. See [docs/Engine.md](docs/Engine.md#models).
- **Reference** shows an image loaded with **File > Load reference image** (PNG, JPEG, BMP
  or TGA), such as a screenshot or a mock-up, stretched across the screen under the HUD, or
  **On top** of it, at the slider's opacity. **Wipe** draws it over the HUD left of a divider
  you drag across the screen, to check an in-game screenshot edge for edge. It is remembered
  between sessions and never saved into a `.hud` file, so a HUD needs no reference element of
  its own.
- Ctrl+wheel zooms at the mouse, Shift+wheel scrolls sideways, and the middle button drags the
  view. The buttons on the right zoom, fit the screen in the view, or show it at one pixel per
  pixel.

## Editing

- **Insert inside** (the Edit menu, or a right-click in the tree) adds a block at the end of the
  selected element or file; **Insert after** adds it right after the selected block. They offer
  every element type the game makes (Group, Bitmap, BitmapMasked, BarBitmap,
  ProceduralBarBitmap, BarSegmented, Text, MultilineText, Model3D, BorderedBox, ObjectiveList,
  VehicleSeating, Map and Target), the seven transforms (GameExt's Math, Lerp and Compare among
  them) and a file's `FileInfo` and `ViewPort`.
- A new block works from the start. An element shows from the start (`EventEnable("initialize")`),
  in the middle of the screen, or at its group's origin inside one. Bars and transforms start on
  the player's health. The map, the targets, the seating chart and the lists come as the stock
  HUD sets them up, templates included.
- What cannot go there is greyed out, and its tooltip says why. Only groups and the elements
  built on one hold elements; transforms go at the top of a file or in a ViewPort; a file has one
  `FileInfo`, at its top. [docs/Engine.md](docs/Engine.md#where-a-block-can-go) has how the game
  reads each.
- **Rename** with F2, a double-click on an element's line in the tree, or the name box at the top
  of the Properties panel. A new block starts with its name selected for typing. The
  `InheritMeshInfo` lines that name a renamed Model3D, and the `TransformNameMesh` lines that
  copy a renamed table, change with it, in one undo step. The status bar points out a name
  another element already has, and an ObjectiveList the game will not fill: it must be named
  `playerNobjectives`. The templates of the map, the targets, the seating chart, a segmented bar
  and a list keep their names, as the game finds them by keyword.
- Dragging an element changes its last `Position` line, in that line's own mode, through the
  inverse of the matrix the game draws it with.
- **Cut**, **Copy** and **Paste** (Ctrl+X, C, V) carry blocks as `.hud` text, so they also go to
  and come from a text editor or another copy of the editor. Pasted blocks go inside the selected
  element when it can hold them, else after it; lines of text paste into the selected block, a
  line it has once replaced. **Duplicate** (Ctrl+D) copies the selection right after itself.
  Copies get names of their own (`name_1`, `name_2`), elements and transforms alike, and so do
  the events an `EventOutput` sends; templates keep theirs.
- **Move up** and **Move down** (Alt+Up, Alt+Down) move the selection among its neighbours.
  Dragging rows in the tree moves them before, after or into another row, between files too;
  the marker shows where they will go, and a row that cannot go there is refused with the reason
  in the status bar. Comments move with their block, re-indented.
- Each of these, whatever it touches, is one undo step (Ctrl+Z, Ctrl+Y).
- The box above the tree filters it: the rows whose name or keyword contains the text, and the
  rows that hold them.
- In the Properties panel, a colour is a swatch that opens a picker and shows the change as you
  pick; a mode, an alignment, a style, a font or an event is a list, which takes typing too; an
  on/off line is a switch. Up and Down step a number (Shift for ten times or 0.1, Ctrl for 0.001).
- A number box works out sums: `5.12*2`, `128/4`, `(1920 - 64) / 2`. A number of pixels, as
  `960px`, is put in the line's own unit, so a Viewport position can be typed in pixels. The
  Events tab's value boxes take sums too.
- **Units.** Choosing another unit for a `Position`, `Rect` or `BitmapRect` (Pixels, Screen,
  Viewport, or Frame for a position) converts its numbers, so the element keeps its place and
  size at the canvas's **Screen**. A line written without a unit shows the one the game reads it
  in, greyed, to change the same way. **Edit > Convert units** does it for the selection and
  everything in it, or a whole file, in one undo step: Viewport makes a HUD follow the
  resolution. Choose the resolution the HUD was made for in **Screen** first. A VehicleSeating's
  own `Position` is left as it is, as the game reads it as pixels whatever its unit.
- The Properties panel's **Add** tab offers every key the game reads for the selected block, by
  the class that reads it (an element's own, its group base, every element's) and GameExt's,
  grouped and with a filter. A template such as `MeshInfo`, `Segment` or a Target template gets
  what its element reads in it, and a Map, Target or VehicleSeating gets back a template it lacks.
  A transform gets its own keys, but not a second source for A or B; another row of its table
  starts as a copy of the last row, right after it.

## Text

Text is laid out as the game lays it out: lines break, wrap and clip in their `TextBox` with
the widths of the game font, read from its `.fff` file, and the box is outlined with the
guides. Each character is the game font's own glyph, with the sixteen levels of coverage the
game's font textures keep. A font that cannot be found is stood in for with a system typeface.

Before any event is simulated, **Text** in the toolbar picks what text elements show:

- **Examples**: the strings of their `Text` keys, with a button in brackets for a `{name}`, and
  for event-driven text what the game would typically send (a weapon name, ammo, a timer as
  `2:05`), numbers printed with the element's own format.
- **Long**: long names, large numbers and paragraphs, to see how a box holds them.
- **Keys**: the keys and event names the file names.

A key the localization lacks shows as the key, as it would in game, and is listed under
**Assets** as a missing string.

## Showing and hiding

What is hidden here is hidden on the canvas only; the files do not change.

- Untick an element in the tree, or press **H**, to hide it and everything in it. A file's tick
  hides its elements.
- **Shift+H**, or a **Ctrl+click** on a tick, shows only that element or file. The other files,
  and every element that neither holds it nor is in it, are hidden; everything in it shows.
- **Alt+H** shows everything again. The tree's right-click menu has these under **Show and
  hide**, with **Show everything in this** for one element or file.
- **Ctrl+click** rows to select several, or **Shift+click** to select every row from the last one
  clicked; a Ctrl+click on the canvas adds an element too. H, Shift+H, the **Show and hide**
  menu, a selected row's tick, the Events tab's **Show selected** and Delete then act on all of
  them, Delete in one undo step. The Properties panel, Rename and Insert act on the last one
  clicked, which the canvas marks with a cross.

## Events

The Events tab lists every event the files and their transforms use: what it is (sent by the
game, by GameExt, or by a transform), its type, and who reads it.

- The clock above the list (▶) runs the HUD over time, at 60 frames a second: fades in and
  out, colours easing back and pulsing, `UseChangeColor` flashes, bars' change strips, and
  GameExt's Lerp easing and Compare hysteresis play out as in the game. A send is made at the
  clock's time; drag the slider to see any moment, or ⏮ to play the sends again from 0. At 0 s
  every send shows what it ends with, fades done, so a value can be checked without running time.
- Type a value and press Enter, or **Send** to send it again so it counts as the latest. What it
  sets shows on the canvas at once: bars fill, texts print their numbers and strings, colours,
  textures and models change, and groups move, scale and turn. Transforms run as the game runs
  them, from every file of the HUD in load order.
- Every element keeps showing, but those hidden in the tree, so one event is enough to see what
  it does to the elements you kept in view.
- **Show and hide as the game does** adds the game's rule for what shows: an element shows only
  once its `EventEnable` event has come after its `EventDisable` one, and only while every group
  above it shows. Nothing shows before any event, as in the game. **Show hidden** on the canvas
  then draws what is not enabled faintly, so it can still be picked.
- **Initialize** sends `initialize`; **On foot** sends a typical spawned soldier's state and turns
  on the game's showing and hiding; **Show selected** sends whatever shows the selected element;
  **Clear** forgets every event sent.
- Until it is sent, groups that follow the same `EventPosition` event are drawn at one point, the
  written `Position` of the first of them, as the game moves them all to the point it sends.
  Their own `Position` lines only matter until that event arrives. With the game's showing and
  hiding on, a group whose event has not come is where it is written.

The rules are in [docs/Engine.md](docs/Engine.md#events).

## Snapshots

The canvas can be rendered to a PNG without opening a window:

```bash
VisualHUD.exe --snapshot file.hud out.png --screen 1920x1080 --game vanilla
```

Options: `--add other.hud` (repeatable: more files of the HUD, loaded after the first, as in
the editor), `--assets folder` (repeatable: asset folders besides the automatic ones and the
editor's saved ones),
`--screen WxH`, `--game gameext|vanilla`, `--snap on|off`, `--viewport n`,
`--background image.png` (draw over a screenshot of the same size), `--no-placeholders`,
`--no-guides`, `--preset onfoot` and `--send event[=value]` (repeatable, in order) to simulate
events, which show and hide elements as the game does unless `--show all` keeps every element
shown, `--text examples|long|keys`, and `--dump layout.txt` to list every element as laid out.
Errors are written to `out.png.error.txt`.

```bash
VisualHUD.exe --snapshot 1playerhud.hud out.png --preset onfoot --send player1.healthFraction=0.3
```

`--ui-snapshot out.png file.hud...` draws the whole editor window instead, off the screen, with
`--ui-size WxH`, `--ui-select name` and `--ui-left elements|events|assets`, to check the interface
without showing it.

## Credits

VisualHUD was started by Surestsafe, who wrote the editor this continues: the `.hud` parser
and writer, mesh and texture loading, localization, undo and the property panel.
