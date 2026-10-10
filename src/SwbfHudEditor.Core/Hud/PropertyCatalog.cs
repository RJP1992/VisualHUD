using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Hud;

/// <summary>
/// The properties the Properties panel's Add tab offers a block: every key the game reads for that
/// block, by the class that reads it (BF2GameExt <c>docs/RE/HUDElementProperties.md</c>: each
/// class's <c>ReadData</c> takes its own keys and passes the rest to its base), plus GameExt's.
/// Each comes with a starting value and a line on what it does. A line already in the block is not
/// offered again; a sub-scope such as <c>MeshInfo</c> can be.
/// </summary>
public static class PropertyCatalog
{
    /// <summary>One property, or sub-scope, that can be added, and the group the Add tab lists it under.</summary>
    public sealed record PropertyDef(string Name, string Group, string Description, Func<HudBlock> Factory)
    {
        /// <summary>A sub-scope (a block with a body) rather than a line.</summary>
        public bool IsBlock { get; init; }

        /// <summary>Whether a block may hold more than one, as <c>MeshInfo</c> or a transform's table rows.</summary>
        public bool Repeatable { get; init; }

        /// <summary>Lines that take its place, so a block with one of them is not offered it: <c>ConstantA</c> or <c>EventInputA</c>.</summary>
        public IReadOnlyList<string> Excludes { get; init; } = Array.Empty<string>();

        internal string Reader { get; init; } = "Element";
    }

    /// <summary>The Add tab's groups, in the order it lists them.</summary>
    public static readonly IReadOnlyList<string> Groups = new[]
    {
        "Placement", "Look", "Show and hide", "Events", "Text", "Bitmap", "Bar", "Model", "Box and list", "Map",
        "Target", "Transform", "File", "GameExt", "Editor",
    };

    private static readonly List<PropertyDef> All = new();

    static PropertyCatalog()
    {
        // ---- Element: every visible element (Element::ReadData, P 005F3E10) ----
        Add("Element", "Placement", "Position", "(x, y, z, mode): where the element sits in its group. Mode Pixels, Screen, Viewport or Frame (a share of the group's Rect); left out, Pixels.",
            () => Leaf("Position", F(0.5), F(0.5), F(0), S("Viewport")));
        Add("Element", "Placement", "Rotation", "(x, y, z) in degrees, turned in that order.",
            () => Leaf("Rotation", F(0), F(0), F(0)));
        Add("Element", "Placement", "Scale", "(x, y, z): stretches the element and everything in it, after the rotation.",
            () => Leaf("Scale", F(1), F(1), F(1)));
        Add("Element", "Placement", "ZOrder", "0 to 255; a lower value draws on top. An element starts at 128, but a child's line moves it in front of its equals even at 128.",
            () => Leaf("ZOrder", I(128)));
        Add("Element", "Placement", "Viewport", "Which player's screen group, 0 to 4 (0 is every screen). Only a top-level element's counts; children follow their group.",
            () => Leaf("Viewport", I(1)));
        Add("Element", "Placement", "Scalable", "1: the element follows the HUD scale video option (ScriptCB_SetHUDScale), keeping its position.",
            () => Leaf("Scalable", I(1)));
        Add("Element", "Look", "Alpha", "Opacity, 0 to 1. A group's alpha reaches its children only with PropagateAlpha(1).",
            () => Leaf("Alpha", F(1)));
        Add("Element", "Look", "Color", "(r, g, b), 0 to 255: tints the element; a group's colour tints everything in it.",
            () => Leaf("Color", I(255), I(255), I(255)));
        Add("Element", "Look", "ColorChange", "(r, g, b): the colour a change flashes to (with UseChangeColor) and a pulse snaps to (with ColorPulseRate).",
            () => Leaf("ColorChange", I(255), I(0), I(0)));
        Add("Element", "Look", "ColorChangeRate", "Seconds the colour takes to ease back after a change or a new EventColor; 0.3 by default.",
            () => Leaf("ColorChangeRate", F(0.3)));
        Add("Element", "Look", "ColorPulseRate", "Seconds between pulses to ColorChange; off by default.",
            () => Leaf("ColorPulseRate", F(1)));
        Add("Element", "Look", "UseChangeColor", "1: the element flashes to ColorChange whenever its value, text, number or texture changes.",
            () => Leaf("UseChangeColor", I(1)));
        Add("Element", "Look", "BlendMode", "\"Additive\" (glow) or \"Alpha\", the default. On a group it reaches only the children read before it.",
            () => Leaf("BlendMode", S("Additive")));
        Add("Element", "Show and hide", "EventEnable", "Shows the element whenever this event is sent, whatever its value. Every element starts hidden.",
            () => Leaf("EventEnable", S("initialize")));
        Add("Element", "Show and hide", "EventDisable", "Hides the element whenever this event is sent. One line only: a second replaces the first.",
            () => Leaf("EventDisable", S("player1.die")));
        Add("Element", "Show and hide", "FadeInTime", "Seconds to fade in when enabled.",
            () => Leaf("FadeInTime", F(0.25)));
        Add("Element", "Show and hide", "FadeHoldTime", "Seconds to stay shown before fading out on its own; forever by default.",
            () => Leaf("FadeHoldTime", F(2.5)));
        Add("Element", "Show and hide", "FadeSustainTime", "Seconds a disable waits before the fade-out starts.",
            () => Leaf("FadeSustainTime", F(0.5)));
        Add("Element", "Show and hide", "FadeOutTime", "Seconds to fade out when disabled or when the hold ends.",
            () => Leaf("FadeOutTime", F(1)));
        Add("Element", "Show and hide", "EventFadeOut", "A new event this element sends when its fade-out starts, for others to react to.",
            () => Leaf("EventFadeOut", S("myElement.fadeOut")));
        Add("Element", "Events", "EventColor", "A Color event that sets the colour, eased over ColorChangeRate; its alpha is ignored.",
            () => Leaf("EventColor", S("player1.message.color")));
        Add("Element", "Events", "EventPulseRate", "A number event that sets the pulse period.",
            () => Leaf("EventPulseRate", S("player1.healthRegenPulseRate")));
        Add("Element", "Events", "EventChanged", "Any event that makes the element flash to ColorChange, as a change would.",
            () => Leaf("EventChanged", S("player1.weapon1.target.hit")));
        Add("Element", "GameExt", "ScreenAnchor", "GameExt, in an AuthoredRatio or TrueWidescreen file: which side of a wider screen a piece keeps to, \"Left\", \"Center\" or \"Right\", or a share from 0 to 1. Only a top-level piece, or a child of a plain container, moves.",
            () => Leaf("ScreenAnchor", S("Right")));
        Add("Element", "Editor", "EditOnly", "1: shown only in the game's own HUD editor.",
            () => Leaf("EditOnly", I(1)));

        // ---- Group base: Group, BarSegmented, MultilineText, BorderedBox, ObjectiveList, Map, Target, VehicleSeating ----
        Add("GroupBase", "Placement", "Rect", "(w, h, h-align, v-align, mode): the group's box. Its alignment makes Position that corner or centre; \"Frame\" positions inside are shares of it; it sizes segmented bars, lists and boxes. Mode Pixels, Screen or Viewport.",
            () => Leaf("Rect", F(0.2), F(0.1), S("Left"), S("Top"), S("Viewport")));
        Add("GroupBase", "Look", "PropagateAlpha", "1: the group's alpha multiplies its children's. Colour always reaches them.",
            () => Leaf("PropagateAlpha", I(1)));
        Add("GroupBase", "Events", "EventPosition", "A Vector3 event that moves the group, read in its Rect's mode and aligned as the Rect says. The game sends screen fractions.",
            () => Leaf("EventPosition", S("player1.weapon1.reticule.position")));
        Add("GroupBase", "Events", "EventScale", "A Vector3 event giving the group's axis lengths; it replaces the authored Scale.",
            () => Leaf("EventScale", S("player1.weapon1.chargeScale")));
        Add("GroupBase", "Events", "EventRotation", "A Vector3 event in degrees that turns the group, keeping its scale.",
            () => Leaf("EventRotation", S("player1.weapon1.chargeRotate")));
        Add("GroupBase", "Events", "EventAlpha", "A Float event setting the group's alpha for one frame (modtools and Steam); GameExt's lerps with OutputIsAlpha(1) send it every frame.",
            () => Leaf("EventAlpha", S("player1.example.alpha")));
        Add("GroupPlayer", "Events", "EventPlayerIndex", "An Int event that points the element at player N.",
            () => Leaf("EventPlayerIndex", S("player1.index")));

        // ---- Text (ElementText::ReadData, P 0060C850); MultilineText's Format template reads these too ----
        Add("Text", "Text", "Text", "A localization key; a key the localization lacks shows as itself.",
            () => Leaf("Text", S("level.common.hud.example")));
        Add("Text", "Text", "EventText", "A String event, or a Uint localization hash, that sets the text.",
            () => Leaf("EventText", S("player1.message")));
        Add("Text", "Text", "EventNumber", "A number event that sets the text, printed with IntegerFormat, FloatFormat, Percent or NumberToTime.",
            () => Leaf("EventNumber", S("player1.weapon1.totalClipBullets")));
        Add("Text", "Text", "TextFont", "The font: gamefont_super_tiny, _tiny, _small, _medium or _large, or a mod's own.",
            () => Leaf("TextFont", S("gamefont_small")));
        Add("Text", "Text", "TextBox", "(w, h) as shares of the view, made whole pixels: the box the text aligns, wraps and clips in, from the position.",
            () => Leaf("TextBox", F(0.2), F(0.05)));
        Add("Text", "Text", "TextScale", "(x, y): scales the glyphs and the line height.",
            () => Leaf("TextScale", F(1), F(1)));
        Add("Text", "Text", "TextAlignment", "(h, v): Left, Right or Center, and Top, Bottom, Center or Baseline, inside the box.",
            () => Leaf("TextAlignment", S("Center"), S("Center")));
        Add("Text", "Text", "TextBreak", "\"Word\" wraps at the box's right edge; \"No Reformat\" counts a run of spaces as one; \"None\" breaks only at line breaks. \"Word Reformat\" reads as Word.",
            () => Leaf("TextBreak", S("Word")));
        Add("Text", "Text", "TextClip", "\"Character\", the only value: a character that would pass the box is not drawn, nor the rest of its line. Always on.",
            () => Leaf("TextClip", S("Character")));
        Add("Text", "Text", "TextStyle", "\"Shadow\" adds a drop shadow; \"Normal\" or \"Selected\" otherwise.",
            () => Leaf("TextStyle", S("Shadow")));
        Add("Text", "Text", "TextCharacterSpacing", "Pixels added after each character; 1 by default.",
            () => Leaf("TextCharacterSpacing", I(0)));
        Add("Text", "Text", "TextTabSpacing", "Pixels between tab stops; eight spaces by default.",
            () => Leaf("TextTabSpacing", I(32)));
        Add("Text", "Text", "IntegerFormat", "The printf format for an Int or Uint EventNumber; %d by default.",
            () => Leaf("IntegerFormat", S("%d")));
        Add("Text", "Text", "FloatFormat", "The printf format for a Float EventNumber; %f by default.",
            () => Leaf("FloatFormat", S("%.0f")));
        Add("Text", "Text", "NumberToTime", "Prints a number of seconds as time: %d days, %h hours, %m minutes, %s seconds.",
            () => Leaf("NumberToTime", S("%m:%02s")));
        Add("Text", "Text", "Percent", "1: prints the number times 100 with a % sign; FloatFormat is ignored.",
            () => Leaf("Percent", I(1)));
        Add("Text", "Text", "InfiniteDashes", "1: an infinite number (0xFFFFFFFF, FLT_MAX) prints -- instead of nothing.",
            () => Leaf("InfiniteDashes", I(1)));

        // ---- Bitmaps (ElementBitmapBase::ReadData, P 005FA670): Bitmap, BitmapMasked, the bars, templates ----
        Add("Bitmap", "Bitmap", "Bitmap", "The texture, by name without extension.",
            () => Leaf("Bitmap", S("hud_texture")));
        Add("Bitmap", "Bitmap", "BitmapRect", "(w, h, h-align, v-align, mode): the quad, its only size; the alignment makes the position that corner or centre. Mode Pixels, Screen or Viewport, read only with all five.",
            () => Leaf("BitmapRect", F(0.1), F(0.1), S("Left"), S("Top"), S("Viewport")));
        Add("Bitmap", "Bitmap", "TexCoords", "(u0, v0, u1, v1): the part of the texture drawn; swap a pair to flip it.",
            () => Leaf("TexCoords", F(0), F(0), F(1), F(1)));
        Add("Bitmap", "Bitmap", "BitmapStyle", "\"Shadow\" draws the shader's shadow; \"Normal\" by default.",
            () => Leaf("BitmapStyle", S("Shadow")));
        Add("Bitmap", "Bitmap", "EventBitmap", "A Texture event, or a Uint name hash, that swaps the texture.",
            () => Leaf("EventBitmap", S("player1.team1.texture")));
        Add("Masked", "Bitmap", "Mask", "BitmapMasked: the mask texture.",
            () => Leaf("Mask", S("map_mask")));
        Add("Masked", "Bitmap", "MaskTexCoords", "BitmapMasked: (u0, v0, u1, v1) of the mask.",
            () => Leaf("MaskTexCoords", F(0), F(0), F(1), F(1)));

        // ---- Bars (ElementBar, P 005F6780): BarBitmap, ProceduralBarBitmap, BarSegmented ----
        Add("Bar", "Bar", "EventValue", "A number event that sets the bar's value.",
            () => Leaf("EventValue", S("player1.healthFraction")));
        Add("Bar", "Bar", "Value", "The value the bar loads with; 1 by default. The stock setup leaves a bar full until its first event.",
            () => Leaf("Value", F(1)));
        Add("Bar", "Bar", "MinValue", "The value that empties the bar; 0 by default.",
            () => Leaf("MinValue", F(0)));
        Add("Bar", "Bar", "MaxValue", "The value that fills the bar; 1 by default.",
            () => Leaf("MaxValue", F(100)));
        Add("BarBitmap", "Bar", "ScaleTexture", "0: the texture is not cropped with the fill, so it squeezes instead; on by default.",
            () => Leaf("ScaleTexture", I(0)));
        Add("BarBitmap", "Bar", "ScaleSize", "0: the value does not move the bar's edge, so the whole quad stays; on by default.",
            () => Leaf("ScaleSize", I(0)));
        Add("BarBitmap", "Bar", "FlashyScale", "The height the change strip eases to during the first second of its fade.",
            () => Leaf("FlashyScale", F(1)));
        Add("BarBitmap", "Bar", "FlashyIncFadeOutTime", "Seconds the change strip fades over when the value grew.",
            () => Leaf("FlashyIncFadeOutTime", F(1)));
        Add("BarBitmap", "Bar", "FlashyDecFadeOutTime", "Seconds the change strip fades over when the value shrank.",
            () => Leaf("FlashyDecFadeOutTime", F(1)));
        Add("BarBitmap", "GameExt", "FillFrom", "GameExt: the end that stays put as the value changes: \"Left\" (stock), \"Right\", \"Bottom\" or \"Top\".",
            () => Leaf("FillFrom", S("Right")));
        Add("Procedural", "Bar", "GlowAlphaBase", "ProceduralBarBitmap: the flicker's lowest alpha share; 0.5.",
            () => Leaf("GlowAlphaBase", F(0.5)));
        Add("Procedural", "Bar", "GlowAlphaScale", "ProceduralBarBitmap: how much random flicker is added to the alpha; 1.",
            () => Leaf("GlowAlphaScale", F(1)));
        Add("Procedural", "Bar", "GlowScaleSize", "ProceduralBarBitmap: how much the glow's size flickers; 0.05.",
            () => Leaf("GlowScaleSize", F(0.05)));
        Add("Segmented", "Bar", "NumSegments", "BarSegmented: how many segments; a value lights a share of them.",
            () => Leaf("NumSegments", I(10)));
        Add("Segmented", "Bar", "Circular", "BarSegmented: 1 (default) sets segments on an ellipse inside the Rect from AngleStart to AngleEnd; 0 lays them in a row across it.",
            () => Leaf("Circular", I(0)));
        Add("Segmented", "Bar", "AngleStart", "BarSegmented: where the ring starts, in degrees.",
            () => Leaf("AngleStart", F(-135)));
        Add("Segmented", "Bar", "AngleEnd", "BarSegmented: where the ring ends, in degrees.",
            () => Leaf("AngleEnd", F(135)));
        Add("Segmented", "Bar", "Segment", "BarSegmented: the template every segment is copied from, a bitmap.",
            () => Block("Segment", "Segment",
                Leaf("Bitmap", S("white")),
                Leaf("BitmapRect", F(4), F(8), S("Center"), S("Center"), S("Pixels"))),
            isBlock: true);

        // ---- Model3D (ElementModel3D::Read, P 00605C70) ----
        Add("Model3D", "Model", "Mesh", "The model, by name.",
            () => Leaf("Mesh", S("hud_model")));
        Add("Model3D", "Model", "EventMesh", "A Model event, or a Uint name hash, that swaps the model; a TransformNameMesh usually sends it.",
            () => Leaf("EventMesh", S("player1.weapon1.mesh")));
        Add("Model3D", "Model", "Lighting", "0: the model is drawn unlit; lit by default.",
            () => Leaf("Lighting", I(0)));
        Add("Model3D", "Model", "Depth", "Sets the model's size: a mesh unit is W / (640 x Depth) pixels across, 0.01 by default. It lasts until an event changes the mesh, which puts it back to 0.01 or to its MeshInfo's Depth.",
            () => Leaf("Depth", F(0.01)));
        Add("Model3D", "Model", "Hardpoint", "Draws the model through this bone's matrix.",
            () => Leaf("Hardpoint", S("hp_bone")));
        Add("Model3D", "Model", "InheritMeshInfo", "Shares an earlier Model3D's MeshInfo table, by that element's name. Each model that reads the table converts its positions again, so write them in Pixels.",
            () => Leaf("InheritMeshInfo", S("player1weapon1model")));
        Add("Model3D", "Model", "MeshInfo", "A model's own placement: when an event sends that model, this block's position, rotation, scale and depth replace the element's. A Mesh line's model keeps the element's own.",
            () => Block("MeshInfo", "hud_model", Leaf("Position", F(0), F(0), F(0), S("Viewport"))),
            isBlock: true, repeatable: true);
        Add("MeshInfo", "Placement", "Position", "(x, y, z, mode): the model's position while it is shown.",
            () => Leaf("Position", F(0), F(0), F(0), S("Viewport")));
        Add("MeshInfo", "Placement", "Rotation", "(x, y, z) in degrees.",
            () => Leaf("Rotation", F(0), F(0), F(0)));
        Add("MeshInfo", "Placement", "Scale", "(x, y, z).",
            () => Leaf("Scale", F(1), F(1), F(1)));
        Add("MeshInfo", "Model", "Depth", "The depth, which sets the size, when an event sends this model; 0.01 without it.",
            () => Leaf("Depth", F(0.01)));

        // ---- Lists and boxes ----
        Add("Multiline", "Box and list", "EventText", "MultilineText: a String event that adds a line.",
            () => Leaf("EventText", S("player1.message")));
        Add("Multiline", "Box and list", "EventColor", "MultilineText: a Color event that sets the next line's colour and alpha.",
            () => Leaf("EventColor", S("player1.message.color")));
        Add("Multiline", "Box and list", "NumLines", "MultilineText: how many lines it shows.",
            () => Leaf("NumLines", I(3)));
        Add("Multiline", "Box and list", "DisplayTime", "MultilineText: seconds a line stays.",
            () => Leaf("DisplayTime", F(3)));
        Add("Multiline", "Box and list", "AlwaysScroll", "MultilineText: 1 scrolls lines away even before the box is full.",
            () => Leaf("AlwaysScroll", I(1)));
        Add("Multiline", "Box and list", "AddToTop", "MultilineText: 1 adds new lines at the top.",
            () => Leaf("AddToTop", I(1)));
        Add("Multiline", "Box and list", "DisableOnEmpty", "MultilineText: 1 hides the element while it has no lines.",
            () => Leaf("DisableOnEmpty", I(1)));
        Add("Multiline", "Box and list", "Format", "MultilineText: the template each line is laid out with, a text.",
            () => Block("Format", "Format", Leaf("TextFont", S("gamefont_small"))),
            isBlock: true);
        Add("Bordered", "Box and list", "Background", "BorderedBox: the texture of its nine-slice box, which its Rect sizes.",
            () => Leaf("Background", S("border_3_pieces")));
        Add("Bordered", "Box and list", "Border", "BorderedBox: (w, h) of the border in texture pixels.",
            () => Leaf("Border", F(2), F(2)));

        // ---- Map (ElementMap::Read, P 006009E0) ----
        Add("Map", "Map", "PositionSmall", "(x, y, z, mode): the minimap's place.", () => Leaf("PositionSmall", F(0.84), F(0.19), F(0), S("Viewport")));
        Add("Map", "Map", "PositionLarge", "(x, y, z, mode): the full map's place.", () => Leaf("PositionLarge", F(0.31), F(0.56), F(0), S("Viewport")));
        Add("Map", "Map", "PositionSpawn", "(x, y, z, mode): the spawn screen map's place.", () => Leaf("PositionSpawn", F(0.3), F(0.48), F(0), S("Viewport")));
        Add("Map", "Map", "BackgroundMaskShapeSmall", "\"Circle\" or \"Rectangle\": the minimap's shape.", () => Leaf("BackgroundMaskShapeSmall", S("Circle")));
        Add("Map", "Map", "BackgroundMaskShapeLarge", "\"Circle\" or \"Rectangle\": the full map's shape.", () => Leaf("BackgroundMaskShapeLarge", S("Rectangle")));
        Add("Map", "Map", "BackgroundMaskShapeSpawn", "\"Circle\" or \"Rectangle\": the spawn map's shape.", () => Leaf("BackgroundMaskShapeSpawn", S("Rectangle")));
        Add("Map", "Map", "AllowMiniMap", "1 allows the minimap mode.", () => Leaf("AllowMiniMap", I(1)));
        Add("Map", "Map", "EventToggleMapMode", "The event that toggles the map's size.", () => Leaf("EventToggleMapMode", S("player1.map.modeToggle")));
        Add("Map", "Map", "EventChangeMapMode", "An Int event that sets the map's mode.", () => Leaf("EventChangeMapMode", S("player1.map.mode")));
        Add("Map", "Map", "EventPostHide", "The event that hides the command posts.", () => Leaf("EventPostHide", S("player1.map.hideCPs")));
        Add("Map", "Map", "EventRefreshTarget", "The event that redraws the targets.", () => Leaf("EventRefreshTarget", S("player1.map.refreshTarget")));
        Add("Map", "Map", "EventRefreshPost", "The event that redraws the posts.", () => Leaf("EventRefreshPost", S("player1.map.refreshPost")));
        Add("Map", "Map", "EventRefreshMarker", "The event that redraws the markers.", () => Leaf("EventRefreshMarker", S("player1.map.refreshMarker")));

        // ---- Target (ElementTarget::Read, P 006098C0) and its templates ----
        Add("Target", "Target", "FourSegmentLockOn", "1: the lock-on is drawn in four segments.", () => Leaf("FourSegmentLockOn", I(1)));
        Add("Target", "Target", "EventResetTargetCommon", "The event that clears the shared targets.", () => Leaf("EventResetTargetCommon", S("targetResetCommon")));
        Add("Target", "Target", "EventResetTargetPlayer", "The event that clears this player's targets.", () => Leaf("EventResetTargetPlayer", S("player1.targetResetPlayer")));
        Add("TargetTemplate", "Target", "MinScale", "The template's scale at the near end of the target's distance range.", () => Leaf("MinScale", F(1)));
        Add("TargetTemplate", "Target", "MaxScale", "The template's scale at the far end of the range.", () => Leaf("MaxScale", F(0.5)));
        Add("TargetTemplate", "Target", "ColorFriendly", "(r, g, b) for a friendly target. An Alpha that rounds to 0 switches the template to team colour.", () => Leaf("ColorFriendly", I(0), I(0), I(255)));

        // ---- The templates of Map and VehicleSeating: the keys the stock templates use ----
        Add("CodeTemplate", "Bitmap", "Bitmap", "The texture, by name.", () => Leaf("Bitmap", S("hud_texture")));
        Add("CodeTemplate", "Bitmap", "TexCoords", "(u0, v0, u1, v1) of the texture.", () => Leaf("TexCoords", F(0), F(0), F(1), F(1)));
        Add("CodeTemplate", "Bitmap", "BitmapStyle", "\"Shadow\" or \"Normal\".", () => Leaf("BitmapStyle", S("Shadow")));
        Add("CodeTemplate", "Bitmap", "Mask", "The mask texture, as the map's backdrops use.", () => Leaf("Mask", S("map_mask")));
        Add("CodeTemplate", "Model", "Mesh", "The model, by name.", () => Leaf("Mesh", S("hud_model")));
        Add("CodeTemplate", "Text", "TextFont", "The font of a text template.", () => Leaf("TextFont", S("gamefont_small")));
        Add("CodeTemplate", "Text", "TextBreak", "\"Word\", \"No Reformat\" or \"None\".", () => Leaf("TextBreak", S("None")));
        Add("CodeTemplate", "Text", "TextAlignment", "(h, v) of a text template.", () => Leaf("TextAlignment", S("Center"), S("Center")));
        Add("CodeTemplate", "Text", "TextStyle", "\"Shadow\", \"Normal\" or \"Selected\".", () => Leaf("TextStyle", S("Shadow")));

        // ---- The templates of Map, Target and VehicleSeating, which their readers find by keyword ----
        foreach (var (mode, view) in new[] { ("Small", "minimap"), ("Large", "full map"), ("Spawn", "spawn screen's map") })
            foreach (var (part, what) in new[]
                     {
                         ("Backdrop", "The map picture, cut by its Mask"), ("PlayerFOV", "The player's view cone, a model"),
                         ("PlayerDirection", "The player's facing, a model"), ("Target", "The template for targets"),
                         ("Post", "The template for command posts"), ("PostText", "The template for command post names"),
                         ("PostSelect", "The template for the chosen post"), ("Marker", "The template for markers"),
                         ("North", "The north pointer"),
                     })
                Add("Map", "Map", part + mode, $"{what} on the {view}.", () => Block(part + mode, part + mode), isBlock: true);
        foreach (var (who, what) in new[]
                 {
                     ("Objective", "an objective"), ("Flag", "a flag"), ("Attacker", "an attacker"), ("Locked", "a locked target"),
                     ("Hint", "a hint"), ("HintFlag", "a hint at a flag"),
                 })
            foreach (var (where, place) in new[] { ("OnScreen", "in view"), ("OffScreen", "off the screen, at its edge") })
                foreach (var (behind, hidden) in new[] { ("", ""), ("Behind", ", hidden behind something") })
                    Add("Target", "Target", who + where + behind, $"The marker for {what} {place}{hidden}.",
                        () => Block(who + where + behind, who + where + behind), isBlock: true);
        foreach (var (part, what) in new[]
                 {
                     ("Backdrop", "Drawn behind the chart"), ("Empty", "An empty seat's icon, a model"), ("EmptyText", "An empty seat's label"),
                     ("Self", "The player's own seat's icon"), ("SelfText", "The player's own seat's label"),
                     ("Player", "Another player's seat's icon"), ("PlayerText", "Another player's seat's label"),
                     ("AI", "An AI's seat's icon"), ("AIText", "An AI's seat's label"),
                 })
            Add("Seating", "Box and list", part, $"VehicleSeating: {what}.", () => Block(part, part), isBlock: true);

        // ---- Transforms (BF2GameExt docs/RE/HUDAuthoring.md, "Transforms") ----
        // The native ones share TransformNumberType's reader: EventInput, EventOutput, InputFactor,
        // EventInputFactor, WrapInput. ColorBlend parses the factors but its input handler never applies them.
        Add("TransformIO", "Transform", "EventInput", "The event the transform reads: a number, or for TransformNameMesh a weapon's name hash.",
            () => Leaf("EventInput", S("player1.healthFraction")));
        Add("TransformIO", "Transform", "EventOutput", "The event it sends, for an element to bind: a new name.",
            () => Leaf("EventOutput", S("player1.example.output")));
        Add("TransformFactor", "Transform", "InputFactor", "A constant the input is multiplied by before the lookup; 1 by default.",
            () => Leaf("InputFactor", F(1)));
        Add("TransformFactor", "Transform", "EventInputFactor", "A number event the input is multiplied by too; it waits for the next input to publish.",
            () => Leaf("EventInputFactor", S("player1.example.factor")));
        Add("TransformWrap", "Transform", "WrapInput", "1: an input past the table's last key wraps round to its first; 0, the default, holds the end.",
            () => Leaf("WrapInput", I(1)));
        Add("TransformColor", "Transform", "NumberColor", "(key, r, g, b): a row of the table, keys rising; inputs between two rows blend their colours.",
            () => Leaf("NumberColor", F(1), I(255), I(255), I(255)), repeatable: true);
        Add("TransformVector3", "Transform", "NumberVector3", "(key, x, y, z): a row of the table, keys rising; inputs between two rows blend their values.",
            () => Leaf("NumberVector3", F(1), F(1), F(1), F(1)), repeatable: true);
        Add("TransformColorBlend", "Transform", "EventBlend", "The Color event the mapped colour blends with; declare the transform sending it first.",
            () => Leaf("EventBlend", S("player1.healthColor")));
        Add("TransformColorBlend", "Transform", "BlendMode", "\"Alpha\", the default, mixes the two colours; \"Additive\" adds the mapped one.",
            () => Leaf("BlendMode", S("Additive")));
        Add("TransformColorBlend", "Transform", "Alpha", "The blend weight, 0 to 1; about 0.5 by default.",
            () => Leaf("Alpha", F(0.5)));
        Add("TransformColorBlend", "Transform", "EventAlpha", "A number event setting the blend weight: a Float 0 to 1, or an Int 0 to 255.",
            () => Leaf("EventAlpha", S("player1.example.weight")));
        Add("TransformNameMesh", "Transform", "NameMesh", "(\"weapon\", \"model\"): a weapon, by name, and the model it shows. Up to 256 rows.",
            () => Leaf("NameMesh", S("all_weap_inf_rifle"), S("hud_all_rifle")), repeatable: true);
        Add("TransformNameMesh", "Transform", "TransformNameMesh", "Copies the rows of a TransformNameMesh declared before this one, by its name.",
            () => Leaf("TransformNameMesh", S("player1weapon1")), repeatable: true);

        // GameExt's Math, Lerp and Compare: A and B are each a constant or an event, never both.
        Add("Math", "Transform", "Operation", "GameExt: \"Add\", \"Subtract\", \"Multiply\", \"Divide\", \"Min\" or \"Max\", of A and B.",
            () => Leaf("Operation", S("Multiply")));
        Add("Compare", "Transform", "Operation", "GameExt: \"Greater\", \"GreaterOrEqual\", \"Less\", \"LessOrEqual\", \"Equal\" or \"NotEqual\": A against B.",
            () => Leaf("Operation", S("Less")));
        Add("Lerp", "Transform", "EventInput", "GameExt: the number event that moves the output from A to B, placed in InputRange.",
            () => Leaf("EventInput", S("player1.healthFraction")));
        Add("Lerp", "Transform", "InputRange", "GameExt: (min, max), each a number or an event: the inputs that give A and B; 0 to 1 by default.",
            () => Leaf("InputRange", F(0), F(1)));
        Add("Operands", "Transform", "ConstantA", "GameExt: A as a number.", () => Leaf("ConstantA", F(0)), excludes: "EventInputA");
        Add("Operands", "Transform", "EventInputA", "GameExt: A as the latest value of a number event.",
            () => Leaf("EventInputA", S("player1.healthFraction")), excludes: "ConstantA");
        Add("Operands", "Transform", "ConstantB", "GameExt: B as a number.", () => Leaf("ConstantB", F(1)), excludes: "EventInputB");
        Add("Operands", "Transform", "EventInputB", "GameExt: B as the latest value of a number event.",
            () => Leaf("EventInputB", S("player1.maxHealth")), excludes: "ConstantB");
        Add("Math", "Transform", "Clamp", "GameExt: (min, max) the result is held to.", () => Leaf("Clamp", F(0), F(1)));
        Add("Lerp", "Transform", "RiseTime", "GameExt: seconds to ease all the way up as the input rises; 0 follows at once.",
            () => Leaf("RiseTime", F(0.25)));
        Add("Lerp", "Transform", "FallTime", "GameExt: seconds to ease all the way down as the input falls; 0 follows at once.",
            () => Leaf("FallTime", F(0.25)));
        Add("Compare", "Transform", "Hysteresis", "GameExt: how far back past B the value must go before the result turns off again.",
            () => Leaf("Hysteresis", F(0.05)));
        Add("Compare", "Transform", "EventOutputTrue", "GameExt: a new Bool event sent as the result turns to 1, for EventEnable.",
            () => Leaf("EventOutputTrue", S("player1.example.on")));
        Add("Compare", "Transform", "EventOutputFalse", "GameExt: a new Bool event sent as the result turns to 0, for EventDisable.",
            () => Leaf("EventOutputFalse", S("player1.example.off")));
        Add("GameExtOutput", "Transform", "EventOutput", "GameExt: the Float event it sends: a new name, which must not exist yet.",
            () => Leaf("EventOutput", S("player1.example.output")));
        Add("GameExtOutput", "Transform", "OutputIsAlpha", "GameExt: 1 sends the result every frame, for a group's EventAlpha, which lasts one frame.",
            () => Leaf("OutputIsAlpha", I(1)));

        // ---- Not elements: FileInfo and ViewPort ----
        Add("FileInfo", "File", "Viewports", "The split-screen view counts the file is for, such as (1) or (1, 2, 3, 4).", () => Leaf("Viewports", I(1)));
        Add("FileInfo", "File", "SplitMode", "The split-screen layouts the file is for, \"Horizontal\" or \"Vertical\" or both: the game loads it only when \"Vertical\" is one.",
            () => Leaf("SplitMode", S("Vertical")));
        Add("FileInfo", "File", "Widescreen", "1 loads the file only with the game's widescreen setting on, 0 only with it off. Modtools takes it as on.",
            () => Leaf("Widescreen", I(1)));
        Add("FileInfo", "GameExt", "AuthoredRatio", "GameExt: (w, h), the screen shape the file was made for, such as (16, 9). The file is laid out at its real height and slid across a wider screen.", () => Leaf("AuthoredRatio", I(16), I(9)));
        Add("FileInfo", "GameExt", "TrueWidescreen", "GameExt: 1 lays a 4:3 file out at its real height on a wider screen; AuthoredRatio for other shapes.", () => Leaf("TrueWidescreen", I(1)));
        Add("ViewPort", "File", "EventNameFilter", "Reads the block again for each player's camera, swapping % in event names for the player's number.", () => Leaf("EventNameFilter", S("player%")));
    }

    private static void Add(string reader, string group, string name, string description, Func<HudBlock> factory,
        bool isBlock = false, bool repeatable = false, string? excludes = null) =>
        All.Add(new PropertyDef(name, group, description, factory)
        {
            Reader = reader, IsBlock = isBlock, Repeatable = repeatable,
            Excludes = excludes is null ? Array.Empty<string>() : new[] { excludes },
        });

    /// <summary>
    /// What can be added to <paramref name="target"/>, in the Add tab's order: what its class reads
    /// (its parent tells a template's class), less the lines it already has.
    /// </summary>
    public static IEnumerable<PropertyDef> GetAddable(HudBlock target, HudBlock? parent = null)
    {
        var readers = ReadersOf(target, parent);
        // Keys hash without case in the game.
        var lines = new HashSet<string>(target.Children.Where(c => !c.HasBody).Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        var blocks = new HashSet<string>(target.Children.Where(c => c.HasBody).Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        // A key two classes read (EventText, EventColor) is offered once: as the subclass, which reads it first, has it.
        var chosen = All
            .Where(d => readers.Contains(d.Reader))
            .Where(d => d.Repeatable || !(d.IsBlock ? blocks : lines).Contains(d.Name))
            .Where(d => !d.Excludes.Any(lines.Contains))
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(d => readers.IndexOf(d.Reader)).First())
            .ToHashSet();
        foreach (var group in Groups)
            foreach (var def in All)
                if (def.Group == group && chosen.Contains(def)) yield return def;
    }

    /// <summary>What the game reads a <paramref name="name"/> line in <paramref name="target"/> as: its group and description, or null.</summary>
    public static PropertyDef? Find(HudBlock target, HudBlock? parent, string name)
    {
        var readers = ReadersOf(target, parent);
        return All.Where(d => readers.Contains(d.Reader) && d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => readers.IndexOf(d.Reader))
            .FirstOrDefault();
    }

    /// <summary>Whether the catalog knows what reads <paramref name="target"/>'s lines, so a line it lacks is one the game ignores.</summary>
    public static bool ReadsLines(HudBlock target, HudBlock? parent) => ReadersOf(target, parent).Count > 0;

    /// <summary>The classes whose ReadData reads a block's lines, the subclass first.</summary>
    private static List<string> ReadersOf(HudBlock target, HudBlock? parent)
    {
        var readers = new List<string>();
        if (ElementKinds.TryGet(target, out var kind))
        {
            // The subclass's own keys first: MultilineText's EventText and EventColor replace Element's.
            switch (kind)
            {
                case ElementKind.Text:
                    readers.Add("Text");
                    break;
                case ElementKind.Bitmap:
                    readers.Add("Bitmap");
                    break;
                case ElementKind.BitmapMasked:
                    readers.AddRange(new[] { "Masked", "Bitmap" });
                    break;
                case ElementKind.BarBitmap:
                    readers.AddRange(new[] { "BarBitmap", "Bar", "Bitmap" });
                    break;
                case ElementKind.ProceduralBarBitmap:
                    readers.AddRange(new[] { "Procedural", "BarBitmap", "Bar", "Bitmap" });
                    break;
                case ElementKind.BarSegmented:
                    readers.AddRange(new[] { "Segmented", "Bar", "GroupBase" });
                    break;
                case ElementKind.Model3D:
                    readers.Add("Model3D");
                    break;
                case ElementKind.MultilineText:
                    readers.AddRange(new[] { "Multiline", "GroupBase" });
                    break;
                case ElementKind.BorderedBox:
                    readers.AddRange(new[] { "Bordered", "GroupBase" });
                    break;
                case ElementKind.Map:
                    readers.AddRange(new[] { "Map", "GroupPlayer", "GroupBase" });
                    break;
                case ElementKind.Target:
                    readers.AddRange(new[] { "Target", "GroupPlayer", "GroupBase" });
                    break;
                case ElementKind.VehicleSeating:
                    readers.AddRange(new[] { "Seating", "GroupPlayer", "GroupBase" });
                    break;
                default:
                    readers.Add("GroupBase");
                    break;
            }
            readers.Add("Element");
            return readers;
        }
        if (!target.HasBody) return readers;

        var transform = target.Name switch
        {
            "TransformNumberColor" => new[] { "TransformColor", "TransformIO", "TransformFactor", "TransformWrap" },
            "TransformNumberVector3" => new[] { "TransformVector3", "TransformIO", "TransformFactor", "TransformWrap" },
            "TransformNumberColorBlend" => new[] { "TransformColorBlend", "TransformColor", "TransformIO", "TransformWrap" },
            "TransformNameMesh" => new[] { "TransformNameMesh", "TransformIO" },
            "TransformNumberMath" => new[] { "Math", "Operands", "GameExtOutput" },
            "TransformNumberLerp" => new[] { "Lerp", "Operands", "GameExtOutput" },
            "TransformNumberCompare" => new[] { "Compare", "Operands", "GameExtOutput" },
            _ => null,
        };
        if (transform is not null)
        {
            readers.AddRange(transform);
            return readers;
        }

        if (target.Name == "FileInfo") readers.Add("FileInfo");
        else if (target.Name.Equals("ViewPort", StringComparison.OrdinalIgnoreCase)) readers.Add("ViewPort");
        else if (parent is not null && ElementKinds.TryGet(parent, out var parentKind))
        {
            switch (parentKind)
            {
                case ElementKind.Model3D when target.Name == "MeshInfo":
                    readers.Add("MeshInfo");
                    break;
                case ElementKind.BarSegmented when target.Name == "Segment":
                    readers.AddRange(new[] { "Bitmap", "Element" });
                    break;
                case ElementKind.MultilineText when target.Name == "Format":
                    readers.AddRange(new[] { "Text", "Element" });
                    break;
                case ElementKind.Target:
                    readers.AddRange(new[] { "TargetTemplate", "Bitmap", "Element" });
                    break;
                case ElementKind.Map:
                case ElementKind.VehicleSeating:
                    readers.AddRange(new[] { "CodeTemplate", "Element" });
                    break;
            }
        }
        return readers;
    }

    private static HudBlock Leaf(string name, params HudArgument[] args)
    {
        var b = new HudBlock { Name = name, HasBody = false };
        foreach (var a in args) b.Arguments.Add(a);
        return b;
    }

    private static HudBlock Block(string name, string argument, params HudBlock[] children)
    {
        var b = new HudBlock { Name = name, HasBody = true };
        b.Arguments.Add(S(argument));
        foreach (var c in children) b.Children.Add(c);
        return b;
    }

    private static HudArgument S(string s) => new(HudArgKind.String, s);
    private static HudArgument I(long n) => HudArgument.FromInt(n);
    private static HudArgument F(double n) => HudArgument.FromFloat(n);
}
