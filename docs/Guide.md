# VisualHUD guide

VisualHUD edits the `.hud` files of Star Wars Battlefront II (2005) and draws the HUD the way the
game does: on the screen size you pick, for the stock game or for the game with BF2GameExt. This
guide has two parts. The first walks through the editor; the second explains how a HUD is put
together, for anyone making one. The README has the full reference for every feature.

---

# Part 1: Using the editor

## The first start

The editor asks where the Battlefront II mod tools are: your `BF2_ModTools` folder. It reads the
stock HUD files, textures, models, fonts and strings from there, so a HUD looks as it does in the
game. If it finds the mod tools at the root of a drive, the folder is filled in already. You can
change it later from **File > Mod tools folder**, or go without: a HUD then shows only what its own
folders and your asset folders hold.

## Opening a HUD

**File > Open** (Ctrl+O) opens one `.hud` file or several. A mod's HUD files are usually in its
`data_XXX\Common\hud` folder. Never edit the stock files in the mod tools' `data` folder: copy the
ones you change into your mod first.

- Several files open together as one HUD, loaded in name order, as the game loads them one after
  another. **File > Add HUD file** adds more.
- The transform files a HUD needs, its folder's `hudtransforms.hud` (or the stock one) and any
  other `hudtransforms*.hud`, load with it and show at the top of the tree.
- **File > New HUD file** (Ctrl+N) starts a new one, laid out the stock way or for a screen shape.

The **Elements** tree shows each file and the elements in it, in the order the file has them. The
canvas in the middle draws them; the **Properties** panel on the right shows the selected element's
lines.

## Looking at the HUD

The bar above the canvas sets what you are looking at:

- **Game**: the stock game (*Vanilla*) or the game with BF2GameExt (*GameExt*). They lay a HUD out
  differently on a wide screen.
- **Screen**: the resolution, from 4:3 to 32:9. Check your HUD at several: something that looks
  right at one size and wrong at another usually has a number in pixels (see Part 2).
- **View**: which of the split-screen views you are looking at.
- **Text**: what texts show before the game sends them anything: examples, long strings to test the
  boxes, or the string keys.
- **Snap**: rounds every element to a whole interface unit, as the stock game does. GameExt draws
  them smoothly, so this is off for it.
- **Outlines** marks what draws nothing (a missing texture, say); **Guides** shows the screen's
  thirds and the stock layout's band; **Hidden** draws faintly what the game would not show yet.
- The zoom buttons fit the screen in the window or show it at its real size.

**A reference image** helps you match a screenshot of the game, or a mock-up. **File > Load
reference image** lays one across the screen. The slider sets how strongly it shows, **On top**
puts it over the HUD, and **Wipe** splits the screen between the image and the HUD with a divider
you can drag. Take the screenshot at the resolution set in **Screen**. **File > Export image** saves
the HUD as a PNG.

## Selecting and moving

- Click an element on the canvas, or its row in the tree. Click the same spot again to pick the
  element under it. Ctrl+click adds to the selection; in the tree, Shift+click picks a range.
- Drag to move. What you drag snaps to the edges and middles of other elements and of the screen,
  shown by pink lines; **Shift** keeps to one direction, **Alt** turns snapping off. Orange lines
  give the gaps to what is nearby. Hold **Alt** over an element to see its gaps without dragging.
- The arrow keys move the selection by a pixel, ten with **Shift**. A run of moves undoes in one go.
- The squares round the selected element resize it: a bitmap's size, a group's frame or a text's
  box. **Shift** keeps the shape.
- **L** locks the selection so the canvas cannot pick or drag it, which lets you get at what is
  under it; **Alt+L** unlocks everything. **H** hides the selection, **Shift+H** shows only it,
  **Alt+H** shows everything. The lock and eye icons in the tree do the same.

## Changing an element

The **Properties** panel lists the selected element's lines, grouped as the game reads them. A line
in grey italics only restates what the game does anyway, and is left out when you save. The **×**
removes a line; the **Add** tab adds the lines the element can take, with what each does.

- Number boxes work out sums (`1920 / 2`) and take pixels (`960px`), written in the line's own
  unit. Up and Down, or the mouse wheel once the box is clicked, step the number: **Shift** steps
  ten times as far, **Alt** a hundred, **Ctrl** a tenth.
- A position or a size has a unit (Pixels, Screen, Viewport, Frame). Picking another one converts
  the numbers so nothing moves. Use Viewport (see Part 2).
- A colour has a swatch that opens a picker, and an eyedropper: click the eyedropper, then the
  canvas, to take the colour there, from the reference image where it shows.
- With several elements selected, the panel shows the lines they all have; a change goes to each.

## Building the HUD

- **Edit > Insert inside** and **Insert after** (also on the tree's right-click menu) add elements,
  transforms and blocks from templates, greyed where the game would not read them.
- **F2** renames; Cut, Copy, Paste and **Duplicate** (Ctrl+D) work on the selection; **Alt+Up** and
  **Alt+Down** move it in its list.
- Drag rows in the tree to move elements into another group. They stay where they are on the
  screen; hold **Alt** as you drop to keep their numbers instead.
- **Edit > Group** puts the selection in a new group (Ctrl+G) or takes a group's elements out
  (Ctrl+Shift+G); **Fit Rect to contents** sizes a group's frame round what it holds. Nothing moves.
- **Repeat** (Ctrl+Shift+D) makes copies in a row, each a step further on, such as a row of icons.
- **Save as snippet** keeps the selection under a name, to insert into any HUD later.
- **Edit > Arrange** lines the selection up, spaces it out evenly, or gives it one size.
- **Fit text box to text** makes a text's box the size of its text, the text staying put.
- **Edit > Convert units** rewrites the selection, or a whole file, in another unit.
- **Edit > Lay out for a screen shape** rewrites a file for GameExt's `AuthoredRatio` so that it
  keeps the look it has now (see Wide screens in Part 2).

Everything undoes with Ctrl+Z, one step for each change.

## Events: seeing the HUD as it plays

In the game, most of a HUD is hidden until the game says otherwise, and bars, numbers and icons
change with what happens. The **Events** tab sends the game's events by hand:

- **On foot** sends what the game has sent by the time a soldier has spawned, and turns on
  **Show and hide as the game does**, so you see what a player sees.
- Type a value next to an event and press Enter to send it: a bar fills, a number prints, an icon
  changes. **Show selected** sends whatever shows the selected element; **Clear** starts over.
- The clock (▶) plays fades, pulses and eased colours over time. **Save** keeps the events you sent
  as a scenario, to send again later.

## Problems, Find and Assets

- The **Problems** tab lists what the game reads differently from what a file seems to say: a line
  the element ignores, a unit that names nothing, an event nothing sends, a texture not found, a
  number in pixels. A click shows the element.
- **Find** (Ctrl+F) and **Replace** (Ctrl+H) search the open files: names, events, textures,
  string keys. A replace is one undo step.
- The **Assets** tab lists the textures, models, fonts and strings the HUD uses, and where each was
  found or that it is missing. **File > Add asset folder** searches another folder for them.

## Saving

**Save** (Ctrl+S) writes every file you changed, and only those, keeping their comments, blank
lines and spacing: only the lines you edited change. A copy of the file as it was goes into a
`_backups` folder beside it first; the newest 20 copies of each file are kept.

---

# Part 2: How a HUD is put together

## Files and loading

A HUD is a set of text files with the `.hud` ending. The game does not read them as they are: the
mod tools munge them into your mod's level files with everything else, so a change shows in the
game only after you munge again.

The game loads the stock transform files and HUD files, then any your mod adds, one after another.
Every file adds its elements to the same screen. Each starts with a `FileInfo` block:

```
FileInfo("myhud")
{
    Viewports(1)
}
```

`Viewports(1)` says the file is for one player on the screen. A file only loads when what its
`FileInfo` asks for matches the game: the number of views (split screen), `SplitMode`, and
`Widescreen`. The Problems tab warns about a `FileInfo` that keeps its file from ever loading.

## Elements and groups

A HUD is made of elements, each a block with a type, a name and its lines:

```
Group("myhud_health")
{
    Viewport(1)
    Position(0.05, 0.9, 0, "Viewport")

    BarBitmap("myhud_health_bar")
    {
        Bitmap("hud_health")
        BitmapRect(0.2, 0.02, "Left", "Center", "Viewport")
        EventValue("player1.healthFraction")
        EventEnable("initialize")
    }
}
```

- **Group** holds other elements and places them all at once. **Bitmap** draws a texture,
  **BarBitmap** a bar that fills, **BarSegmented** a bar or a ring of segments, **Text** a string,
  **Model3D** a 3D model such as the weapon icon. Others draw maps, lists, markers and the vehicle
  seats.
- An element at the top of a file says which view it is in with `Viewport(1)`; the elements inside
  a group take the group's.
- Give every element a name of its own: lines that find an element by name find the first one.
- What draws over what is set by `ZOrder`: a lower number draws on top. Between equal numbers, the
  order of the file decides.

## Placing things: positions, sizes and units

Every element has a place, `Position(x, y, z, unit)`, and most have a size: a bitmap's
`BitmapRect(width, height, horizontal alignment, vertical alignment, unit)`, a group's
`Rect(...)` of the same form, a text's `TextBox(width, height)`. The numbers mean different things
in each unit:

- **Viewport**: shares of the view. `0.5` across is the middle; `0.1` wide is a tenth of the view.
  These grow and shrink with the screen, so a HUD in Viewport looks the same at every resolution.
- **Screen**: shares of the whole screen. The same as Viewport with one player.
- **Frame**: shares of the parent group's `Rect`, for an element inside a group.
  `Position(0.5, 0.5, 0, "Frame")` is the middle of the group's frame.
- **Pixels**: a number of screen pixels. They do not grow with the screen: a HUD laid out in pixels
  at 1280 × 720 is in the wrong place at 1920 × 1080. A `Position` without a unit is in pixels.

**Use Viewport, or Frame inside a group with a Rect.** The editor never writes pixels itself, and the
Problems tab points out the pixel numbers left in a file. **Edit > Convert units > To Viewport**
fixes them without moving anything; set **Screen** to the resolution the HUD was made for first.

A group's `Rect` is its frame. Its alignment says which point of the frame sits at the group's
`Position`: with `"Center", "Center"` the frame's middle is there, and the elements inside are
placed from the frame's top left corner. A bitmap's `BitmapRect` alignment works the same way for
its picture.

## Wide screens

The stock game lays a HUD out for a 4:3 screen and fits it to a wider one: it squeezes the HUD
into a band and stretches bitmaps taller. A HUD made for 16:9 that way never looks quite as drawn.
**Guides** shows the band.

With BF2GameExt, a `FileInfo` line says which screen shape the file was made for:

```
FileInfo("myhud")
{
    Viewports(1)
    AuthoredRatio(16, 9)
}
```

GameExt then lays the file out at its real height for that shape, nothing stretched. On a wider
screen, each element at the top of the file moves out to its side, by the third of the screen its
place is in. `ScreenAnchor("Left")`, `"Center"` or `"Right"` picks the side when its third is not
the one you want, such as a centred bar whose left edge is in the left third.

**Edit > Lay out for a screen shape** rewrites a stock file for `AuthoredRatio` so it keeps the look
it has now. Set **Game** to GameExt and **Screen** to a few shapes (16:9, 21:9, 32:9) to check it.

## Showing and hiding: events

The game tells the HUD what is going on by sending events: `player1.spawn` when you spawn,
`player1.healthFraction` with your health from 0 to 1, `player1.die` when you die. `initialize` is
sent as the HUD starts. Elements listen for them:

- An element starts hidden. `EventEnable("player1.spawn")` shows it when that event is sent;
  `EventDisable("player1.die")` hides it again. `EventEnable("initialize")` shows it from the start.
  An element only shows while every group above it shows.
- `EventValue` fills a bar, `EventText` and `EventNumber` set a text, `EventColor` a colour,
  `EventBitmap` a texture, `EventMesh` a model; a group can follow `EventPosition`, `EventScale`
  and `EventRotation`.
- **Transforms** turn events into new ones: a number scaled or wrapped, a number into a colour, a
  weapon's name into its model. The stock `hudtransforms.hud` holds the game's own; yours go at the top of
  a file. BF2GameExt adds more events and three transforms of its own: see its documentation.

The Events tab lists every event your files use, who sends it and who reads it. If the Problems tab
says an event is sent by nothing, check the name: a misspelt event just never comes.

## Text

A `Text` element shows a string from the game's localization by its key, `Text("key")`, or what an
event sends it. `TextFont` names the font (a `.fff` file), `TextBox` its box in shares of the view,
`TextAlignment` how the lines sit in the box, and `TextScale` makes it larger or smaller.

The game draws text at its font's size in pixels, whatever the resolution. Everything else in a
Viewport HUD grows with the screen, so text looks smaller at higher resolutions. Size your text at
the resolution you test most, and check it at the others.

## Models

A `Model3D` draws a model from the mod's `.msh` files, such as the weapon in your hands. `Mesh`
names the model; a `MeshInfo` block for each model the game may send places and sizes that one.
`InheritMeshInfo` lets a second model share the first one's `MeshInfo` table. A shared table's
positions must stay in pixels, because each model that reads it converts them again.

## Testing in the game

Munge your mod and play it. The mod tools' game, `BF2_modtools.exe`, writes what the HUD loader
complains about to its log: "Error reading parameter" means a line the element does not read, and
"unable to find event" an event that does not exist. The Problems tab catches most of these first.

## With and without BF2GameExt

The stock game skips the lines and events it does not know, such as `AuthoredRatio`,
`ScreenAnchor` or `FillFrom`, logs them, and carries on: those can stay in files the stock game
loads too, which then look as they do without GameExt. GameExt's three transforms are the exception:
`TransformNumberMath`, `TransformNumberLerp` and `TransformNumberCompare` are blocks the stock game
cannot skip, and can crash it. Keep them in a file that only a game with GameExt loads.

## A checklist before you share a HUD

- Every position and size in Viewport, or Frame inside a group: the Problems tab shows none in pixels.
- It looks right at several **Screen** sizes, and under both games if both will load it.
- **On foot** in the Events tab shows what a player sees, and what shows and hides when.
- No problems left that you did not mean, and no assets missing.
- Munged and tried in the game.
