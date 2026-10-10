# VisualHUD

An editor for Star Wars Battlefront II (2005) `.hud` files that draws a HUD the way the game
does, on the screen you pick, for the stock game or for the game with
[BF2GameExt](https://github.com/S1thK3nny/BF2GameExt).

The layout follows the engine's own code rather than estimates: positions, Rects and
alignment, the 640 × 480 mapping and the letterbox a wide screen gets, the bitmap stretch,
bar fills, segmented rings, draw order and rounding. How each rule was read is in
[docs/Engine.md](docs/Engine.md), along with what is not modelled yet.

## Build and run

Needs the .NET 8 SDK (or newer) on Windows.

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
  - strings: the `Text` keys, found in the localization or missing;
  - the localization file.
- **Rescan** reads the folders again after files were added to them; the × removes an added
  folder.
- Textures are `.tga` files and models `.msh` files, as the game's munge reads them; a `.png`
  does not count. A font gives text its widths; its glyph images are not read yet.

## The canvas

- **Game**: *GameExt* honours `AuthoredRatio`, `TrueWidescreen`, `ScreenAnchor` and
  `FillFrom`; *Vanilla* ignores them, as the stock game does, and stretches a HUD on a
  wide screen.
- **Screen**: the resolution the game draws at. With Shader Patch's display scaling that is
  the resolution the game sees (1280×720 on a 4K screen at 150%), and it decides how big a
  `"Pixels"` value is.
- **Viewport**: whose screen group to show.
- **Unit snapping**: the stock draw snaps every element to 1/640 of the width; GameExt's
  `HudSubPixel` (on by default) does not. Switching game resets it.
- **Guides**: the band the stock game squashes the HUD into on a wide screen, or, for a
  file GameExt lays out, the thirds of its layout and where each lands.
- **HUD scale** resizes `Scalable(1)` elements, as the game's video option does.
- **Model size** tunes the estimated size of `Model3D` meshes.
- **Reference** shows an image loaded with **File > Load reference image** (PNG, JPEG, BMP
  or TGA), such as a screenshot or a mock-up, stretched across the screen under the HUD, or
  **On top** of it, at the slider's opacity. It is remembered between sessions and never
  saved into a `.hud` file, so a HUD needs no reference element of its own.

Dragging an element changes its last `Position` line, in that line's own mode, through
the inverse of the matrix the game draws it with.

The Properties panel's **Add** tab offers every key the game reads for the selected block, by
the class that reads it (an element's own, its group base, every element's) and GameExt's,
grouped and with a filter. A template such as `MeshInfo`, `Segment` or a Target template gets
what its element reads in it.

## Text

Text is laid out as the game lays it out: lines break, wrap and clip in their `TextBox` with
the widths of the game font, read from its `.fff` file, and the box is outlined with the
guides. The characters themselves are drawn with a system typeface until the font images are
read.

Before any event is simulated, **Text** in the toolbar picks what text elements show:

- **Examples**: the strings of their `Text` keys, with a button in brackets for a `{name}`, and
  for event-driven text what the game would typically send (a weapon name, ammo, a timer as
  `2:05`), numbers printed with the element's own format.
- **Long**: long names, large numbers and paragraphs, to see how a box holds them.
- **Keys**: the keys and event names the file names.

A key the localization lacks shows as the key, as it would in game, and is listed under
**Assets** as a missing string.

## Events

The Events tab lists every event the files and their transforms use: what it is (sent by the
game, by GameExt, or by a transform), its type, and who reads it. With **Simulate the game's
events** on, the canvas shows the HUD as the game would after the events you have sent:

- An element shows only once its `EventEnable` event has come after its `EventDisable` one,
  and only while every group above it shows. Nothing shows before any event, as in the game.
- Bars fill, texts print their numbers and strings, colours, textures and models change, and
  groups move, scale and turn, from the values sent. Transforms run as the game runs them, from
  every file of the HUD in load order.
- Type a value and press Enter, or **Send** to send it again so it counts as the latest.
  **Initialize** sends `initialize`; **On foot** sends a typical spawned soldier's state;
  **Show selected** sends whatever shows the selected element; **Clear** starts again.
- **Show hidden** on the canvas draws what is not enabled faintly, so it can still be picked.

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
events, `--text examples|long|keys`, and `--dump layout.txt` to list every element as laid out. Errors are written to
`out.png.error.txt`.

```bash
VisualHUD.exe --snapshot 1playerhud.hud out.png --preset onfoot --send player1.healthFraction=0.3
```

## Credits

VisualHUD was started by Surestsafe, who wrote the editor this continues: the `.hud` parser
and writer, mesh and texture loading, localization, undo and the property panel.
