# Keeping VisualHUD in step with BF2GameExt

VisualHUD previews a HUD the way the game draws it, with [BF2GameExt](https://github.com/S1thK3nny/BF2GameExt)
or without it. That only holds while VisualHUD knows everything GameExt changes about the HUD. So the rule is:
**a GameExt change that touches the HUD is not finished until VisualHUD has it too.**

## What counts

Anything in GameExt that changes what a `.hud` file can say, what the game sends to a HUD, or how a HUD is
laid out or drawn:

- a new or changed line or block: an element property, a `FileInfo` line, a transform or one of its parameters;
- a new or changed event that GameExt sends;
- a change to layout or drawing: wide screens, anchors, rounding, bars, text, models, blending;
- an INI setting that changes any of these, or a new default for one;
- a fix to how the stock game treats a HUD (VisualHUD shows the fixed behaviour under **Game: GameExt** and the
  stock behaviour under **Vanilla**).

When in doubt, it counts.

## The rule

A change in that list is done when all of these hold:

1. GameExt's HUD reference, `docs/user/HUD_PROPERTIES.md`, lists it with the version it arrives in. That page is
   the contract between the two projects.
2. VisualHUD reads it, previews it and checks it, or says in [Engine.md](Engine.md) why it does not ("Not
   modelled").
3. The parity test passes (see below).
4. VisualHUD's version goes up, and its release notes say which GameExt version it matches.

GameExt work and its VisualHUD side are done together, in the same piece of work. If they have to be split, the
VisualHUD side is recorded as a task before the GameExt side is called finished.

## Where things go in VisualHUD

- **A property or `FileInfo` line**: `Core/Hud/PropertyCatalog.cs` (the entry, the class that reads it, its
  description and an example), the element's reading in `ElementReader` (`Core/Layout/ElementSpec.cs`), its
  effect in `Core/Layout/HudLayout.cs`, its choices in `Core/Hud/PropertyArguments.cs`, its default in
  `Core/Layout/ElementDefaults.cs` if leaving it out on save is safe, and misuse in `Core/Problems/HudProblems.cs`.
- **An event**: `Core/Events/HudEventCatalog.cs`, in the GameExt section, with its type and a note; the **On foot**
  preset in `Core/Events/HudEventPresets.cs` if a spawned soldier has it; and what it drives in the simulation if
  that is something new.
- **A transform**: a port of GameExt's code in `Core/Events/HudTransformGraph.cs`, tested with GameExt's own test
  vectors; its readers in `PropertyCatalog`; its template in `Core/Hud/HudBlockTemplates.cs` for the Insert menus.
  A transform the stock game cannot read can crash it, so the Problems tab and the guide say to keep it in files
  only GameExt loads.
- **Layout or drawing**: `Core/Layout`. `TrueWidescreen`, `BarFill` and the rest are line-for-line ports of
  GameExt's cores: port the change, with its test vectors, and write the rule in [Engine.md](Engine.md).
- **An INI setting**: what the canvas does under GameExt, or a canvas option; and the setting's line in
  `GameExtParityTests`, saying what VisualHUD does with it.
- **For users**: the README for the reference, [Guide.md](Guide.md) when HUD authors need to know.

## Checking

- **The parity test**, `tests/SwbfHudEditor.Core.Tests/GameExtParityTests.cs`, reads GameExt's HUD reference from
  the BF2GameExt checkout beside this repository (or the folder `VISUALHUD_GAMEEXT` names) and fails for any event,
  property, transform, transform parameter or INI setting there that VisualHUD does not know. Without a GameExt
  checkout it has nothing to check.
- **Unit tests** port GameExt's own test vectors for the maths.
- **In the game**: render the changed HUD with `--snapshot` at two or three screen sizes, take screenshots in the
  game at the same sizes, and compare them on the canvas with the reference image and **Wipe**. A group whose
  `EventPosition` comes from a transform shows where the transform puts it, not its own `Position`, so check those
  numbers too.

## Status

VisualHUD 1.0.0 matches the HUD features of BF2GameExt up to 1.2.0 (unreleased as of 11 October 2026). Two INI
settings act only while the game runs, so there is nothing for the editor to preview: `[Features]
TargetBarLatchSeconds` and `[Fixes] ReticleCorrection`.
