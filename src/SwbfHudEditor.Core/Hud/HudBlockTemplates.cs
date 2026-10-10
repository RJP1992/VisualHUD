using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Hud;

/// <summary>What a template makes: an element, a transform, or a block of the file itself.</summary>
public enum TemplateFamily
{
    Element,
    Transform,
    File,
}

/// <summary>
/// A block the Insert menus add, with a starting setup that works in the game: an element shows from
/// the start (<c>EventEnable("initialize")</c>), a transform reads an event the game sends, and the
/// game-driven elements (the map, the targets, the lists) come as the stock HUD sets them up.
/// </summary>
public sealed class BlockTemplate
{
    private readonly string _source;

    internal BlockTemplate(string keyword, string category, TemplateFamily family, string baseName, string description,
        string source, bool gameExt = false)
    {
        Keyword = keyword;
        Category = category;
        Family = family;
        BaseName = baseName;
        Description = description;
        GameExt = gameExt;
        _source = source;
    }

    public string Keyword { get; }

    /// <summary>The menu group it is listed under.</summary>
    public string Category { get; }

    public TemplateFamily Family { get; }

    /// <summary>The name a new one starts from, before it is made unique.</summary>
    public string BaseName { get; }

    public string Description { get; }

    /// <summary>A GameExt class: the stock game skips it.</summary>
    public bool GameExt { get; }

    /// <summary>
    /// A name for a new one that <paramref name="taken"/> does not have, or null when there is none:
    /// an <c>ObjectiveList</c> must be named <c>playerNobjectives</c>, and every player may already have one.
    /// </summary>
    public string? NewName(Func<string, bool> taken)
    {
        if (Keyword == "ObjectiveList")
        {
            for (var player = 1; player <= 4; player++)
                if (!taken($"player{player}objectives")) return $"player{player}objectives";
            return null;
        }
        if (!taken(BaseName)) return BaseName;
        for (var i = 1; i < 100_000; i++)
            if (!taken($"{BaseName}_{i}")) return $"{BaseName}_{i}";
        return null;
    }

    /// <summary>
    /// A new block named <paramref name="name"/>. <paramref name="nested"/>: it goes inside an element, so
    /// it sits at its group's origin and follows the group's screen; otherwise it starts at the middle of
    /// player 1's screen. A transform publishes <c>player1.</c> and its name.
    /// </summary>
    public HudBlock Create(string name, bool nested)
    {
        var player = Keyword == "ObjectiveList" && name.StartsWith("player", StringComparison.OrdinalIgnoreCase) &&
                     name.Length > 6 && char.IsDigit(name[6]) ? name[6].ToString() : "1";
        var text = _source.Replace("{name}", name).Replace("{output}", $"player1.{name}").Replace("{player}", player);
        var block = HudParser.Parse(text).TopLevel[0];
        ForgetTrivia(block);
        if (nested && Family == TemplateFamily.Element)
        {
            // HUD::Element::GetViewport asks the group, so a child's own Viewport line changes nothing.
            for (var i = block.Children.Count - 1; i >= 0; i--)
                if (!block.Children[i].HasBody && block.Children[i].Name == "Viewport") block.Children.RemoveAt(i);
            if (block.Children.FirstOrDefault(c => !c.HasBody && c.Name == "Position") is { Arguments.Count: >= 2 } position)
            {
                position.Arguments[0] = HudArgument.FromFloat(0);
                position.Arguments[1] = HudArgument.FromFloat(0);
            }
        }
        return block;
    }

    /// <summary>A new block is written in the file's own indentation, as the writer lays out a block it made.</summary>
    private static void ForgetTrivia(HudBlock block)
    {
        block.Trivia = null;
        foreach (var child in block.Children) ForgetTrivia(child);
    }

    public override string ToString() => Keyword;
}

/// <summary>
/// The blocks the Insert menus offer, and where each may go. The rules follow how the game reads a
/// file: <c>HUD::Manager::Load</c> makes an item of every block at the top of a file;
/// <c>ViewPort::Read</c> (P <c>0061EDF0</c>) makes one of every block in its scope, once per camera;
/// a group (<c>ElementGroupBase::Read</c>, P <c>005FC310</c>, and the Map, Target, MultilineText and
/// VehicleSeating readers once their own templates are matched) hands every other block to
/// <c>ReadChild</c> (P <c>005FC3F0</c>), which keeps it as a child only when it is an element. Any other
/// element (<c>Item::Read</c>, P <c>00617D10</c>) reads a block in its body as more lines of its own.
/// </summary>
public static class HudBlockTemplates
{
    /// <summary>The menu groups, in the order the menus list them.</summary>
    public static readonly IReadOnlyList<string> Categories = new[]
    {
        "Groups", "Bitmaps", "Bars", "Text", "Models", "Boxes and lists", "Map and targets", "Transforms", "File",
    };

    public static IReadOnlyList<BlockTemplate> All { get; } = Build();

    public static BlockTemplate? Find(string keyword) =>
        All.FirstOrDefault(t => t.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Why nothing can be inserted into <paramref name="container"/>, or null when something can. Null
    /// <paramref name="container"/> is the top of a file, where everything can go.
    /// </summary>
    public static string? WhyNothingFits(HudBlock? container)
    {
        if (container is null || IsViewPort(container)) return null;
        if (ElementKinds.TryGet(container, out var kind))
            return ElementKinds.IsGroupBased(kind)
                ? null
                : $"A {container.Name} holds no elements. Groups, and the elements built on one (BarSegmented, " +
                  "MultilineText, BorderedBox, ObjectiveList, Map, Target and VehicleSeating), do.";
        return container.HasBody
            ? $"{container.Name} holds only its own lines."
            : $"{container.Name} is a line, not a block.";
    }

    /// <summary>
    /// Why a new block of <paramref name="template"/> cannot go into <paramref name="container"/> (null:
    /// the top of <paramref name="document"/>), or null when it can.
    /// </summary>
    public static string? WhyNot(BlockTemplate template, HudBlock? container, HudDocument document)
    {
        if (WhyNothingFits(container) is { } nothing) return nothing;
        if (template.Keyword == "FileInfo")
        {
            if (document.TopLevel.Any(b => b.HasBody && b.Name.Equals("FileInfo", StringComparison.OrdinalIgnoreCase)))
                return "The file has a FileInfo already. The game reads one, at the top of the file.";
            return container is null ? null : "FileInfo goes at the top of its file.";
        }
        return template.Family switch
        {
            TemplateFamily.File when container is not null => $"{template.Keyword} goes at the top of a file.",
            TemplateFamily.Transform when container is not null && !IsViewPort(container) =>
                "A transform goes at the top of a file or in a ViewPort, as hudtransforms.hud keeps its own.",
            _ => null,
        };
    }

    /// <summary>
    /// Why <paramref name="block"/>, pasted or dragged, cannot go into <paramref name="container"/> (null:
    /// the top of <paramref name="document"/>), or null when it can: the same rules as a new block of
    /// its kind. A template of an element, such as a map's <c>BackdropSmall</c>, goes only back into an
    /// element of that kind.
    /// </summary>
    public static string? WhyNot(HudBlock block, HudBlock? container, HudDocument document)
    {
        if (!block.HasBody) return $"{block.Name} is a line: it goes into an element's properties.";
        if (Find(block.Name) is { } template)
        {
            if (template.Keyword != "FileInfo") return WhyNot(template, container, document);
            // Moving a file's own FileInfo within it is fine; a second one is not.
            if (container is not null) return "FileInfo goes at the top of its file.";
            return document.TopLevel.Any(b => !ReferenceEquals(b, block) && b.HasBody &&
                                              b.Name.Equals("FileInfo", StringComparison.OrdinalIgnoreCase))
                ? "The file has a FileInfo already. The game reads one, at the top of the file."
                : null;
        }
        // A template or sub-scope: only where it was, in an element of the kind that reads it.
        return container is not null && ElementKinds.TryGet(container, out _) &&
               (container.Children.Contains(block) ||
                PropertyCatalog.GetAddable(container).Any(d => d.IsBlock && d.Name.Equals(block.Name, StringComparison.OrdinalIgnoreCase)))
            ? null
            : $"{block.Name} is a template of an element: it goes only into an element that reads it.";
    }

    private static bool IsViewPort(HudBlock block) =>
        block.HasBody && block.Name.Equals("ViewPort", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<BlockTemplate> Build()
    {
        const TemplateFamily E = TemplateFamily.Element;
        const TemplateFamily T = TemplateFamily.Transform;
        const TemplateFamily F = TemplateFamily.File;
        return new BlockTemplate[]
        {
            new("Group", "Groups", E, "new_group",
                "Holds other elements, and moves, scales, fades, shows and hides them together.",
                """
                Group("{name}")
                {
                    PropagateAlpha(1)
                    Viewport(1)
                    Position(0.500000, 0.500000, 0.000000, "Viewport")
                    EventEnable("initialize")
                }
                """),

            new("Bitmap", "Bitmaps", E, "new_bitmap",
                "A texture on a quad, sized by its BitmapRect.",
                """
                Bitmap("{name}")
                {
                    Bitmap("hud_white")
                    BitmapRect(0.100000, 0.100000, "Center", "Center", "Viewport")
                    Viewport(1)
                    Position(0.500000, 0.500000, 0.000000, "Viewport")
                    EventEnable("initialize")
                }
                """),
            new("BitmapMasked", "Bitmaps", E, "new_bitmapmasked",
                "A texture cut out by a second, mask texture. No stock HUD uses one; the map's backdrops mask the same way.",
                """
                BitmapMasked("{name}")
                {
                    Bitmap("hud_white")
                    Mask("map_mask")
                    BitmapRect(0.100000, 0.100000, "Center", "Center", "Viewport")
                    Viewport(1)
                    Position(0.500000, 0.500000, 0.000000, "Viewport")
                    EventEnable("initialize")
                }
                """),

            new("BarBitmap", "Bars", E, "new_barbitmap",
                "A bar that fills with a number event; it starts on the player's health.",
                """
                BarBitmap("{name}")
                {
                    EventValue("player1.healthFraction")
                    Bitmap("hud_white")
                    BitmapRect(0.200000, 0.020000, "Left", "Center", "Viewport")
                    Viewport(1)
                    Position(0.400000, 0.500000, 0.000000, "Viewport")
                    EventEnable("initialize")
                }
                """),
            new("ProceduralBarBitmap", "Bars", E, "new_proceduralbarbitmap",
                "A bar whose glow flickers at random, as the hero bar's pulse; it starts on the player's health.",
                """
                ProceduralBarBitmap("{name}")
                {
                    ScaleTexture(0)
                    EventValue("player1.healthFraction")
                    Bitmap("hud_white")
                    BitmapRect(0.200000, 0.020000, "Left", "Center", "Viewport")
                    BlendMode("Additive")
                    Viewport(1)
                    Position(0.400000, 0.500000, 0.000000, "Viewport")
                    EventEnable("initialize")
                }
                """),
            new("BarSegmented", "Bars", E, "new_barsegmented",
                "A bar of segments copied from its Segment template, on a ring or in a row; it starts as the stock clip ring, on the player's health.",
                """
                BarSegmented("{name}")
                {
                    AngleStart(-135.000000)
                    AngleEnd(135.000000)
                    NumSegments(30)
                    EventValue("player1.healthFraction")
                    Rect(0.087831, 0.111404, "Center", "Center", "Viewport")
                    PropagateAlpha(1)
                    Viewport(1)
                    Position(0.500000, 0.500000, 0.000000, "Viewport")
                    EventEnable("initialize")
                    Segment("Segment")
                    {
                        Bitmap("hud_clipbar_segment")
                        BitmapRect(0.003122, 0.014606, "Center", "Center", "Viewport")
                    }
                }
                """),

            new("Text", "Text", E, "new_text",
                "Text from the localization, or from a text or number event, laid out in its TextBox.",
                """
                Text("{name}")
                {
                    Text("game.objectives.updated")
                    TextFont("gamefont_small")
                    TextBox(0.300000, 0.050000)
                    TextAlignment("Center", "Center")
                    Viewport(1)
                    Position(0.350000, 0.475000, 0.000000, "Viewport")
                    EventEnable("initialize")
                }
                """),
            new("MultilineText", "Text", E, "new_multilinetext",
                "A list of messages from a text event, each laid out by its Format template, as the stock system messages.",
                """
                MultilineText("{name}")
                {
                    EventText("player1.message")
                    EventColor("player1.message.color")
                    NumLines(4)
                    DisplayTime(5.000000)
                    Rect(0.400000, 0.200000, "Left", "Top", "Viewport")
                    PropagateAlpha(1)
                    Viewport(1)
                    Position(0.300000, 0.400000, 0.000000, "Viewport")
                    EventEnable("initialize")
                    Format("Format")
                    {
                        TextBox(0.400000, 0.050000)
                        TextBreak("Word")
                        TextStyle("Shadow")
                        TextAlignment("Left", "Center")
                        TextFont("gamefont_small")
                        FadeInTime(0.250000)
                        FadeOutTime(0.250000)
                    }
                }
                """),

            new("Model3D", "Models", E, "new_model3d",
                "A 3D model, as the weapon icons; EventMesh swaps it.",
                """
                Model3D("{name}")
                {
                    Mesh("hud_infinite_ammo_icon")
                    Viewport(1)
                    Position(0.500000, 0.500000, 0.000000, "Viewport")
                    EventEnable("initialize")
                }
                """),

            new("BorderedBox", "Boxes and lists", E, "new_borderedbox",
                "A box drawn from a nine-slice texture and sized by its Rect, as the stock tooltip box. It holds other elements.",
                """
                BorderedBox("{name}")
                {
                    Background("tooltipsborder")
                    Border(1.000000, 1.000000)
                    Rect(0.300000, 0.200000, "Center", "Center", "Viewport")
                    PropagateAlpha(1)
                    Viewport(1)
                    Position(0.500000, 0.500000, 0.000000, "Viewport")
                    EventEnable("initialize")
                }
                """),
            new("ObjectiveList", "Boxes and lists", E, "player1objectives",
                "The objectives, which the game writes in itself; the objectives key shows it. The game fills only the list named playerNobjectives.",
                """
                ObjectiveList("{name}")
                {
                    Rect(0.348704, 0.400000, "Right", "Top", "Viewport")
                    PropagateAlpha(1)
                    Viewport({player})
                    Position(0.970468, 0.082377, 0.000000, "Viewport")
                    FadeInTime(0.250000)
                    FadeOutTime(0.250000)
                    EventEnable("player{player}.objectivelist.enable")
                    EventDisable("player{player}.objectivelist.disable")
                }
                """),
            new("VehicleSeating", "Boxes and lists", E, "new_vehicleseating",
                "The vehicle's seats and who sits in them, from the stock seating chart's templates.",
                """
                VehicleSeating("{name}")
                {
                    EventPlayerIndex("player1.index")
                    PropagateAlpha(1)
                    Viewport(1)
                    Position(0.500000, 0.500000, 0.000000, "Viewport")
                    EventEnable("initialize")
                    Backdrop("Backdrop")
                    {
                    }
                    Empty("Empty")
                    {
                        Mesh("hud_vehicle_seatingchart_empty_icon")
                        Position(-0.017477, 0.000000, 0.000000, "Viewport")
                        Scale(0.300000, 0.300000, 0.300000)
                        Alpha(0.500000)
                    }
                    EmptyText("EmptyText")
                    {
                        TextBreak("Word")
                        TextFont("gamefont_tiny")
                        TextStyle("Shadow")
                        Alpha(0.500000)
                    }
                    Self("Self")
                    {
                        Mesh("hud_vehicle_seatingchart_player1_icon")
                        Position(-0.017477, 0.000000, 0.000000, "Viewport")
                        Scale(0.300000, 0.300000, 0.300000)
                    }
                    SelfText("SelfText")
                    {
                        TextBreak("Word")
                        TextFont("gamefont_small")
                        TextStyle("Shadow")
                    }
                    Player("Player")
                    {
                        Mesh("hud_vehicle_seatingchart_player1_icon")
                        Position(-0.017477, 0.000000, 0.000000, "Viewport")
                        Scale(0.300000, 0.300000, 0.300000)
                        Alpha(0.500000)
                    }
                    PlayerText("PlayerText")
                    {
                        TextBreak("Word")
                        TextFont("gamefont_tiny")
                        TextStyle("Shadow")
                        Alpha(0.500000)
                    }
                    AI("AI")
                    {
                        Mesh("hud_vehicle_seatingchart_ai_icon")
                        Position(-0.017477, 0.000000, 0.000000, "Viewport")
                        Scale(0.300000, 0.300000, 0.300000)
                        Alpha(0.750000)
                    }
                    AIText("AIText")
                    {
                        TextBreak("Word")
                        TextFont("gamefont_tiny")
                        TextStyle("Shadow")
                        Alpha(0.500000)
                    }
                }
                """),

            new("Map", "Map and targets", E, "new_map",
                "The minimap, the full map and the spawn map, with the stock templates for the backdrop, posts, targets and markers of each.",
                """
                Map("{name}")
                {
                    PositionSmall(0.917484, 0.110884, 0.004111, "Viewport")
                    PositionLarge(0.308639, 0.556811, -38.133076, "Viewport")
                    PositionSpawn(0.767984, 0.477156, 0.000000, "Viewport")
                    EventToggleMapMode("player1.map.modeToggle")
                    EventChangeMapMode("player1.map.mode")
                    EventPostHide("player1.map.hideCPs")
                    EventRefreshTarget("player1.map.refreshTarget")
                    EventRefreshPost("player1.map.refreshPost")
                    EventRefreshMarker("player1.map.refreshMarker")
                    EventPlayerIndex("player1.index")
                    PropagateAlpha(1)
                    Viewport(1)
                    Position(0.917484, 0.110884, 0.004111, "Viewport")
                    Scale(0.820000, 0.820000, 0.820000)
                    ZOrder(127)
                    Alpha(0.850000)
                    EventEnable("player1.map.enable")
                    EventDisable("player1.map.disable")
                    BackdropSmall("BackdropSmall")
                    {
                        Mask("map_mask")
                        TexCoords(0.248419, 0.564873, 0.470641, 0.787095)
                        ZOrder(255)
                        Alpha(0.500000)
                    }
                    PlayerFOVSmall("PlayerFOVSmall")
                    {
                        Mesh("hud_map_fov")
                        ZOrder(123)
                    }
                    PlayerDirectionSmall("PlayerDirectionSmall")
                    {
                        Mesh("hud_map_fov")
                        ZOrder(124)
                        Alpha(0.200000)
                        Color(255, 255, 0)
                    }
                    TargetSmall("TargetSmall")
                    {
                        BitmapStyle("Shadow")
                        ZOrder(126)
                        Color(0, 0, 0)
                    }
                    PostSmall("PostSmall")
                    {
                        BitmapStyle("Shadow")
                    }
                    PostTextSmall("PostTextSmall")
                    {
                        TextBreak("Word")
                        TextAlignment("Center", "Bottom")
                        ZOrder(127)
                    }
                    PostSelectSmall("PostSelectSmall")
                    {
                        Bitmap("hud_icon_flash")
                        BitmapStyle("Shadow")
                    }
                    MarkerSmall("MarkerSmall")
                    {
                        BitmapStyle("Shadow")
                        ZOrder(0)
                    }
                    NorthSmall("NorthSmall")
                    {
                        Bitmap("hud_map_northpointer")
                        BitmapStyle("Shadow")
                        Position(0.000000, -0.110000, 0.000000, "Viewport")
                        Scale(2.000000, 2.000000, 2.000000)
                        ZOrder(127)
                    }
                    BackdropLarge("BackdropLarge")
                    {
                        Mask("map_mask")
                        ZOrder(255)
                        Alpha(0.500000)
                    }
                    PlayerFOVLarge("PlayerFOVLarge")
                    {
                        Mesh("hud_large_map_fov")
                        ZOrder(0)
                        Alpha(0.500000)
                    }
                    PlayerDirectionLarge("PlayerDirectionLarge")
                    {
                        Mesh("hud_map_fov")
                        ZOrder(124)
                        Alpha(0.200000)
                        Color(255, 255, 0)
                    }
                    TargetLarge("TargetLarge")
                    {
                        BitmapStyle("Shadow")
                        ZOrder(254)
                        Color(0, 0, 0)
                    }
                    PostLarge("PostLarge")
                    {
                        BitmapStyle("Shadow")
                        ZOrder(0)
                    }
                    PostTextLarge("PostTextLarge")
                    {
                        TextBreak("Word")
                        TextAlignment("Center", "Bottom")
                        ZOrder(127)
                    }
                    PostSelectLarge("PostSelectLarge")
                    {
                        Bitmap("hud_icon_flash")
                        BitmapStyle("Shadow")
                    }
                    MarkerLarge("MarkerLarge")
                    {
                        BitmapStyle("Shadow")
                        ZOrder(0)
                    }
                    NorthLarge("NorthLarge")
                    {
                        Bitmap("hud_map_northpointer")
                        BitmapStyle("Shadow")
                        Position(0.000000, -0.375000, 0.000000, "Viewport")
                        Scale(5.000000, 5.000000, 5.000000)
                        ZOrder(127)
                    }
                    BackdropSpawn("BackdropSpawn")
                    {
                        Mask("map_mask")
                        ZOrder(255)
                        Alpha(0.500000)
                    }
                    PlayerFOVSpawn("PlayerFOVSpawn")
                    {
                        ZOrder(123)
                    }
                    PlayerDirectionSpawn("PlayerDirectionSpawn")
                    {
                        ZOrder(124)
                    }
                    TargetSpawn("TargetSpawn")
                    {
                        BitmapStyle("Shadow")
                        ZOrder(126)
                        Color(0, 0, 0)
                    }
                    PostSpawn("PostSpawn")
                    {
                        BitmapStyle("Shadow")
                    }
                    PostTextSpawn("PostTextSpawn")
                    {
                        TextBreak("Word")
                        TextAlignment("Center", "Bottom")
                        ZOrder(127)
                    }
                    PostSelectSpawn("PostSelectSpawn")
                    {
                        Bitmap("hud_icon_flash")
                        BitmapStyle("Shadow")
                    }
                    MarkerSpawn("MarkerSpawn")
                    {
                        BitmapStyle("Shadow")
                        ZOrder(0)
                    }
                    NorthSpawn("NorthSpawn")
                    {
                        Bitmap("hud_map_northpointer")
                        BitmapStyle("Shadow")
                        Position(0.000000, -0.325000, 0.000000, "Viewport")
                        Scale(5.000000, 5.000000, 5.000000)
                        ZOrder(127)
                    }
                }
                """),
            new("Target", "Map and targets", E, "new_target",
                "The markers the game puts on objectives, flags, attackers, lock-ons and hints in the world, with the stock templates.",
                """
                Target("{name}")
                {
                    EventResetTargetCommon("targetResetCommon")
                    EventResetTargetPlayer("player1.targetResetPlayer")
                    FourSegmentLockOn(1)
                    EventPlayerIndex("player1.index")
                    Viewport(1)
                    EventEnable("player1.spawn")
                    EventDisable("player1.die")
                    ObjectiveOnScreen("ObjectiveOnScreen")
                    {
                        ColorFriendly(0, 0, 255)
                        MinScale(1.000000)
                        MaxScale(0.500000)
                        Bitmap("hud_objective_icon")
                        BitmapRect(0.050000, 0.060000, "Center", "Bottom", "Viewport")
                    }
                    ObjectiveOnScreenBehind("ObjectiveOnScreenBehind")
                    {
                        ColorFriendly(0, 0, 255)
                        MinScale(1.000000)
                        MaxScale(0.500000)
                        Bitmap("hud_objective_icon_hidden")
                        BitmapRect(0.050000, 0.060000, "Center", "Bottom", "Viewport")
                        Alpha(0.900000)
                    }
                    ObjectiveOffScreen("ObjectiveOffScreen")
                    {
                        ColorFriendly(0, 0, 255)
                        MinScale(1.000000)
                        MaxScale(0.500000)
                        Bitmap("hud_objective_icon_offscreen")
                        BitmapRect(0.050000, 0.060000, "Center", "Center", "Viewport")
                    }
                    ObjectiveOffScreenBehind("ObjectiveOffScreenBehind")
                    {
                        ColorFriendly(0, 0, 255)
                        MinScale(1.000000)
                        MaxScale(0.500000)
                        Bitmap("hud_objective_icon_offscreen")
                        BitmapRect(0.050000, 0.060000, "Center", "Center", "Viewport")
                        Alpha(0.500000)
                    }
                    FlagOnScreen("FlagOnScreen")
                    {
                        ColorFriendly(0, 0, 255)
                        MinScale(1.000000)
                        MaxScale(0.500000)
                        Bitmap("hud_target_flag_onscreen")
                        BitmapRect(0.050000, 0.060000, "Center", "Bottom", "Viewport")
                        Alpha(0.900000)
                    }
                    FlagOnScreenBehind("FlagOnScreenBehind")
                    {
                        ColorFriendly(0, 0, 255)
                        MinScale(1.000000)
                        MaxScale(0.500000)
                        Bitmap("hud_target_flag_onscreen")
                        BitmapRect(0.050000, 0.060000, "Center", "Bottom", "Viewport")
                        Alpha(0.200000)
                    }
                    FlagOffScreen("FlagOffScreen")
                    {
                        ColorFriendly(0, 0, 255)
                        MinScale(1.000000)
                        MaxScale(0.500000)
                        Bitmap("hud_objective_icon_offscreen")
                        BitmapRect(0.050000, 0.060000, "Center", "Center", "Viewport")
                    }
                    FlagOffScreenBehind("FlagOffScreenBehind")
                    {
                        ColorFriendly(0, 0, 255)
                        MinScale(1.000000)
                        MaxScale(0.500000)
                        Bitmap("hud_objective_icon_offscreen")
                        BitmapRect(0.050000, 0.060000, "Center", "Center", "Viewport")
                        Alpha(0.500000)
                    }
                    AttackerOnScreen("AttackerOnScreen")
                    {
                        ColorFriendly(0, 0, 255)
                        MinScale(1.000000)
                        MaxScale(0.250000)
                        Bitmap("hud_target_hint_onscreen")
                        BitmapRect(0.050000, 0.060000, "Center", "Bottom", "Viewport")
                        Position(0.000000, -0.050000, 0.000000, "Viewport")
                    }
                    AttackerOnScreenBehind("AttackerOnScreenBehind")
                    {
                        ColorFriendly(0, 0, 255)
                        MinScale(1.000000)
                        MaxScale(0.500000)
                        Bitmap("hud_target_hint_hidden")
                        BitmapRect(0.050000, 0.060000, "Center", "Bottom", "Viewport")
                        Position(0.000000, -0.050000, 0.000000, "Viewport")
                    }
                    AttackerOffScreen("AttackerOffScreen")
                    {
                        ColorFriendly(0, 0, 255)
                        MinScale(1.000000)
                        MaxScale(0.500000)
                        Bitmap("hud_target_hint_offscreen")
                        BitmapRect(0.050000, 0.060000, "Center", "Center", "Viewport")
                        Alpha(0.500000)
                    }
                    AttackerOffScreenBehind("AttackerOffScreenBehind")
                    {
                        ColorFriendly(0, 0, 255)
                        MinScale(1.000000)
                        MaxScale(0.500000)
                        Bitmap("hud_target_hint_offscreen")
                        BitmapRect(0.050000, 0.060000, "Center", "Center", "Viewport")
                        Alpha(0.500000)
                    }
                    LockedOnScreen("LockedOnScreen")
                    {
                        ColorFriendly(0, 0, 255)
                        MinScale(3.000000)
                        MaxScale(0.000000)
                        Bitmap("hud_target_onscreen")
                        BitmapRect(0.025000, 0.030000, "Right", "Bottom", "Viewport")
                        TexCoords(0.000000, 0.000000, 0.500000, 0.500000)
                        Color(255, 50, 50)
                    }
                    LockedOnScreenBehind("LockedOnScreenBehind")
                    {
                        ColorFriendly(0, 0, 255)
                        MinScale(3.000000)
                        MaxScale(0.000000)
                        Bitmap("hud_target_hidden")
                        BitmapRect(0.025000, 0.030000, "Right", "Bottom", "Viewport")
                        TexCoords(0.000000, 0.000000, 0.500000, 0.500000)
                        Alpha(0.600000)
                        Color(200, 90, 90)
                    }
                    LockedOffScreen("LockedOffScreen")
                    {
                        ColorFriendly(0, 0, 255)
                        MinScale(1.600000)
                        MaxScale(1.400000)
                        Bitmap("hud_target_pointer_solid")
                        BitmapRect(0.050000, 0.060000, "Center", "Center", "Viewport")
                        Color(255, 50, 50)
                    }
                    LockedOffScreenBehind("LockedOffScreenBehind")
                    {
                        ColorFriendly(0, 0, 255)
                        MinScale(1.600000)
                        MaxScale(1.400000)
                        Bitmap("hud_target_pointer")
                        BitmapRect(0.050000, 0.060000, "Center", "Center", "Viewport")
                        Color(200, 90, 90)
                    }
                    HintOnScreen("HintOnScreen")
                    {
                        ColorFriendly(0, 86, 213)
                        MinScale(1.000000)
                        MaxScale(0.500000)
                        Bitmap("hud_target_hint_onscreen")
                        BitmapRect(0.050000, 0.060000, "Center", "Bottom", "Viewport")
                        Position(0.000000, -0.050000, 0.000000, "Viewport")
                        Color(175, 0, 0)
                    }
                    HintOnScreenBehind("HintOnScreenBehind")
                    {
                        ColorFriendly(0, 86, 213)
                        MinScale(1.000000)
                        MaxScale(0.500000)
                        Bitmap("hud_target_hint_onscreen")
                        BitmapRect(0.050000, 0.060000, "Center", "Bottom", "Viewport")
                        Position(0.000000, -0.050000, 0.000000, "Viewport")
                        Alpha(0.400000)
                        Color(175, 0, 0)
                    }
                    HintOffScreen("HintOffScreen")
                    {
                        ColorFriendly(0, 0, 255)
                        Alpha(0.000000)
                    }
                    HintOffScreenBehind("HintOffScreenBehind")
                    {
                        ColorFriendly(0, 0, 255)
                        Alpha(0.000000)
                    }
                    HintFlagOnScreen("HintFlagOnScreen")
                    {
                        ColorFriendly(0, 86, 213)
                        MinScale(1.500000)
                        MaxScale(0.750000)
                        Bitmap("hud_target_flag_onscreen")
                        BitmapRect(0.050000, 0.060000, "Center", "Bottom", "Viewport")
                        Position(0.000000, -0.050000, 0.000000, "Viewport")
                        Color(175, 0, 0)
                    }
                    HintFlagOnScreenBehind("HintFlagOnScreenBehind")
                    {
                        ColorFriendly(0, 86, 213)
                        MinScale(1.500000)
                        MaxScale(0.750000)
                        Bitmap("hud_target_flag_onscreen")
                        BitmapRect(0.050000, 0.060000, "Center", "Bottom", "Viewport")
                        Position(0.000000, -0.050000, 0.000000, "Viewport")
                        Alpha(0.400000)
                        Color(175, 0, 0)
                    }
                    HintFlagOffScreen("HintFlagOffScreen")
                    {
                        ColorFriendly(0, 86, 213)
                        MinScale(1.200000)
                        MaxScale(0.900000)
                        Bitmap("hud_objective_icon_offscreen")
                        BitmapRect(0.050000, 0.060000, "Center", "Center", "Viewport")
                        Color(175, 0, 0)
                    }
                    HintFlagOffScreenBehind("HintFlagOffScreenBehind")
                    {
                        ColorFriendly(0, 86, 213)
                        MinScale(1.200000)
                        MaxScale(0.900000)
                        Bitmap("hud_objective_icon_offscreen")
                        BitmapRect(0.050000, 0.060000, "Center", "Center", "Viewport")
                        Alpha(0.500000)
                        Color(175, 0, 0)
                    }
                }
                """),

            new("TransformNumberColor", "Transforms", T, "new_numbercolor",
                "Turns a number event into a Color event for EventColor, through a table of keys; it starts as a health tint.",
                """
                TransformNumberColor("{name}")
                {
                    NumberColor(0.000000, 255, 30, 20)
                    NumberColor(0.250000, 255, 30, 20)
                    NumberColor(1.000000, 255, 255, 255)
                    EventInput("player1.healthFraction")
                    EventOutput("{output}")
                }
                """),
            new("TransformNumberVector3", "Transforms", T, "new_numbervector3",
                "Turns a number event into a Vector3 for a group's EventPosition, EventScale or EventRotation; it starts as a scale that shrinks with health.",
                """
                TransformNumberVector3("{name}")
                {
                    NumberVector3(0.000000, 0.700000, 0.700000, 1.000000)
                    NumberVector3(1.000000, 1.000000, 1.000000, 1.000000)
                    EventInput("player1.healthFraction")
                    EventOutput("{output}")
                }
                """),
            new("TransformNumberColorBlend", "Transforms", T, "new_colorblend",
                "Maps a number to a colour through a table, then blends it with a Color event; it starts as a health tint softened against the stock health colour.",
                """
                TransformNumberColorBlend("{name}")
                {
                    NumberColor(0.000000, 255, 30, 20)
                    NumberColor(1.000000, 0, 150, 255)
                    EventBlend("player1.healthColor")
                    BlendMode("Alpha")
                    Alpha(0.500000)
                    EventInput("player1.healthFraction")
                    EventOutput("{output}")
                }
                """),
            new("TransformNameMesh", "Transforms", T, "new_namemesh",
                "Turns a weapon's name into a model for a Model3D's EventMesh; it starts with the stock weapon table, from hudtransforms.hud.",
                """
                TransformNameMesh("{name}")
                {
                    TransformNameMesh("player1weapon1")
                    EventInput("player1.weapon1.change")
                    EventOutput("{output}")
                }
                """),
            new("TransformNumberMath", "Transforms", T, "new_math",
                "GameExt: A plus, minus, times, over, the smaller or the larger of B, each an event or a constant; it starts as health in percent.",
                """
                TransformNumberMath("{name}")
                {
                    Operation("Multiply")
                    EventInputA("player1.healthFraction")
                    ConstantB(100.000000)
                    EventOutput("{output}")
                }
                """, gameExt: true),
            new("TransformNumberLerp", "Transforms", T, "new_lerp",
                "GameExt: eases between two values as its input moves, for fades and flashes; it starts as health, eased over a quarter second.",
                """
                TransformNumberLerp("{name}")
                {
                    EventInput("player1.healthFraction")
                    RiseTime(0.250000)
                    FallTime(0.250000)
                    EventOutput("{output}")
                }
                """, gameExt: true),
            new("TransformNumberCompare", "Transforms", T, "new_compare",
                "GameExt: 1 or 0 as A passes B, and Bool events as it turns, for warnings; it starts as health under a quarter.",
                """
                TransformNumberCompare("{name}")
                {
                    Operation("Less")
                    EventInputA("player1.healthFraction")
                    ConstantB(0.250000)
                    EventOutput("{output}")
                }
                """, gameExt: true),

            new("FileInfo", "File", F, "hud",
                "The file's header: the split-screen views it is for, and GameExt's AuthoredRatio or TrueWidescreen. It goes first in the file.",
                """
                FileInfo("{name}")
                {
                    Viewports(1)
                }
                """),
            new("ViewPort", "File", F, "Transforms",
                "A scope the game reads once per player, renaming the player1 events for each, as hudtransforms.hud holds its transforms.",
                """
                ViewPort("{name}")
                {
                    EventNameFilter("player%")
                }
                """),
        };
    }
}
