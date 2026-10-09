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
with a `.hud` file as its argument. Textures, models, the transforms file and the English
localization are looked up next to the file and in `C:\BF2_ModTools\data\Common`.

```bash
dotnet test SwbfHudEditor.sln
```

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

Dragging an element changes its last `Position` line, in that line's own mode, through
the inverse of the matrix the game draws it with.

## Events

The Events tab lists every event the file and its transforms use: what it is (sent by the
game, by GameExt, or by a transform), its type, and who reads it. With **Simulate the game's
events** on, the canvas shows the HUD as the game would after the events you have sent:

- An element shows only once its `EventEnable` event has come after its `EventDisable` one,
  and only while every group above it shows. Nothing shows before any event, as in the game.
- Bars fill, texts print their numbers and strings, colours, textures and models change, and
  groups move, scale and turn, from the values sent. Transforms run as the game runs them, from
  the folder's `hudtransforms*.hud` files and the file itself.
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

Options: `--screen WxH`, `--game gameext|vanilla`, `--snap on|off`, `--viewport n`,
`--background image.png` (draw over a screenshot of the same size), `--no-placeholders`,
`--no-guides`, `--preset onfoot` and `--send event[=value]` (repeatable, in order) to simulate
events, and `--dump layout.txt` to list every element as laid out. Errors are written to
`out.png.error.txt`.

```bash
VisualHUD.exe --snapshot 1playerhud.hud out.png --preset onfoot --send player1.healthFraction=0.3
```

## Credits

VisualHUD was started by Surestsafe, who wrote the editor this continues: the `.hud` parser
and writer, mesh and texture loading, localization, undo and the property panel.
