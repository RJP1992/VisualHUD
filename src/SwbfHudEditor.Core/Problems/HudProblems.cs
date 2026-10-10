using System.Text.RegularExpressions;
using SwbfHudEditor.Core.Assets;
using SwbfHudEditor.Core.Events;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Problems;

/// <summary>How much a problem matters: the game ignores or breaks it; it is likely not what was meant; worth knowing.</summary>
public enum ProblemLevel
{
    Error,
    Warning,
    Note,
}

/// <summary>
/// Something the game reads otherwise than the file seems to say. <see cref="Block"/> is the block it is
/// in (an element, a template, a transform), to show; <see cref="Line"/> the line, when it is one line's.
/// </summary>
public sealed record HudProblem(ProblemLevel Level, HudDocument Document, HudBlock Block, HudBlock? Line, string Message);

/// <summary>The checks of the Problems tab, each from how the game reads the files (docs/Engine.md).</summary>
public static partial class HudProblems
{
    /// <summary>
    /// The problems of <paramref name="checkedDocuments"/>, file by file, top to bottom.
    /// <paramref name="allDocuments"/> is everything the HUD loads with them, in load order (its transform
    /// files too): what sends an event, which model a table is inherited from, which names are taken.
    /// <paramref name="missing"/>, when the assets were looked for, tells a texture, model or font that
    /// was not found.
    /// </summary>
    public static List<HudProblem> Check(IReadOnlyList<HudDocument> checkedDocuments, IReadOnlyList<HudDocument> allDocuments,
        Func<HudAssetKind, string, bool>? missing = null)
    {
        var graph = HudTransformGraph.From(allDocuments);
        var uses = HudEventUsage.Of(allDocuments, graph);
        // The game and the stock transforms send each player's events, catalogued as player 1's, and a
        // ViewPort's % stands for each player's number.
        var sent = uses.Where(u => u.Source != HudEventSource.Unknown).Select(u => AnyPlayer(u.Name))
            .Concat(HudEventCatalog.All.Select(e => AnyPlayer(e.Name)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unsent = uses.Where(u => u.Source == HudEventSource.Unknown && !sent.Contains(AnyPlayer(u.Name)))
            .Select(u => u.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var context = new Context(allDocuments, unsent, missing);
        var found = new List<HudProblem>();
        foreach (var document in checkedDocuments)
        {
            void Add(ProblemLevel level, HudBlock block, HudBlock? line, string message) =>
                found.Add(new HudProblem(level, document, block, line, message));
            foreach (var block in document.TopLevel.Where(b => b.HasBody)) Walk(block, null, null, document, context, Add);
        }
        return found;
    }

    private delegate void Report(ProblemLevel level, HudBlock block, HudBlock? line, string message);

    private sealed class Context(IReadOnlyList<HudDocument> all, HashSet<string> unsent, Func<HudAssetKind, string, bool>? missing)
    {
        public HashSet<string> Unsent { get; } = unsent;

        // Each block's element at the top of its file or ViewPort, whose Viewport line counts.
        private readonly Dictionary<HudBlock, HudBlock> _top = Tops(all);

        // Each block's place in the order the game makes them: file by file, top to bottom.
        private readonly Dictionary<HudBlock, int> _order = Order(all);

        /// <summary>Whether <paramref name="block"/> is made before <paramref name="other"/>.</summary>
        public bool Before(HudBlock block, HudBlock other) =>
            _order.TryGetValue(block, out var a) && _order.TryGetValue(other, out var b) && a < b;

        /// <summary>The viewport an element shows in: its top element's Viewport line, 0 for every one.</summary>
        public int ViewportOf(HudBlock block) =>
            _top.TryGetValue(block, out var top) && LastLine(top, "Viewport") is { } line ? (int)EngineArgs.FloatAt(line, 0) : 0;

        private static Dictionary<HudBlock, int> Order(IReadOnlyList<HudDocument> all)
        {
            var order = new Dictionary<HudBlock, int>(ReferenceEqualityComparer.Instance);
            foreach (var block in Blocks(all.SelectMany(d => d.TopLevel))) order[block] = order.Count;
            return order;
        }

        private static Dictionary<HudBlock, HudBlock> Tops(IReadOnlyList<HudDocument> all)
        {
            var tops = new Dictionary<HudBlock, HudBlock>(ReferenceEqualityComparer.Instance);
            void Mark(HudBlock block, HudBlock top)
            {
                tops[block] = top;
                foreach (var child in block.Children.Where(c => c.HasBody)) Mark(child, top);
            }
            void Scope(IEnumerable<HudBlock> blocks)
            {
                foreach (var block in blocks.Where(b => b.HasBody))
                    if (IsViewPort(block)) Scope(block.Children);
                    else Mark(block, block);
            }
            foreach (var document in all) Scope(document.TopLevel);
            return tops;
        }
        public Func<HudAssetKind, string, bool>? Missing { get; } = missing;

        // Every element and transform by name, in load order: the first is the one a name finds.
        public Dictionary<string, List<(HudDocument Document, HudBlock Block)>> Named { get; } = NamedItems(all);

        private static Dictionary<string, List<(HudDocument, HudBlock)>> NamedItems(IReadOnlyList<HudDocument> all)
        {
            var named = new Dictionary<string, List<(HudDocument, HudBlock)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var document in all)
                foreach (var block in Blocks(document.TopLevel))
                    if (IsNamedItem(block) && NameOf(block) is { Length: > 0 } name)
                    {
                        if (!named.TryGetValue(name, out var list)) named[name] = list = new List<(HudDocument, HudBlock)>();
                        list.Add((document, block));
                    }
            return named;
        }
    }

    // ---- The walk -----------------------------------------------------------------------------------------

    private static void Walk(HudBlock block, HudBlock? parent, HudBlock? grandparent, HudDocument document, Context context, Report add)
    {
        var isElement = ElementKinds.TryGet(block, out var kind);
        Placement(block, parent, grandparent, document, isElement, add);
        if (isElement) Element(block, kind, parent, context, add);
        else if (block.Name == "FileInfo") FileInfo(block, add);
        if (IsNamedItem(block)) Names(block, document, context, add);

        var readsLines = PropertyCatalog.ReadsLines(block, parent);
        foreach (var line in block.Children.Where(c => !c.HasBody))
        {
            if (readsLines && PropertyCatalog.Find(block, parent, line.Name) is null)
                add(ProblemLevel.Warning, block, line, $"{Describe(block)} reads no {line.Name} line: the game ignores it.");
            else if (readsLines) Units(block, line, add);
            Assets(block, line, context, add);
        }
        // A MeshInfo places a model only an event sends, such as a weapon's: it matters only once that is sent.
        if (block.Name == "MeshInfo" && Missing(context, HudAssetKind.Mesh, StringAt(block, 0)))
            add(ProblemLevel.Note, block, null, $"Model \"{StringAt(block, 0)}\" is not found in the HUD's folders, the stock ones or an asset folder. It draws only when an event sends it, as a weapon's.");

        foreach (var child in block.Children.Where(c => c.HasBody)) Walk(child, block, parent, document, context, add);
    }

    /// <summary>A block where the game makes nothing of it, or makes it where the editor does not look.</summary>
    private static void Placement(HudBlock block, HudBlock? parent, HudBlock? grandparent, HudDocument document, bool isElement, Report add)
    {
        if (isElement || HudBlockTemplates.Find(block.Name) is not null)
        {
            if (HudBlockTemplates.WhyNot(block, parent, document) is not { } why) return;
            var transform = block.Name.StartsWith("Transform", StringComparison.Ordinal);
            add(transform ? ProblemLevel.Note : isElement ? ProblemLevel.Error : ProblemLevel.Warning, block, null,
                transform ? $"{why} The game makes it all the same; the editor's preview does not read it there." : $"{why} The game makes nothing of it here.");
            return;
        }
        // A template: only in an element that reads it.
        if (parent is null)
            add(ProblemLevel.Warning, block, null, $"{block.Name} is not an element: at the top of a file the game makes nothing of it.");
        else if (PropertyCatalog.ReadsLines(parent, grandparent) && PropertyCatalog.Find(parent, grandparent, block.Name) is null)
            add(ProblemLevel.Warning, block, null, $"{Describe(parent)} reads no {block.Name} block: the game ignores it.");
    }

    // ---- Units -------------------------------------------------------------------------------------------

    /// <summary>A unit the game reads otherwise than it is written.</summary>
    private static void Units(HudBlock block, HudBlock line, Report add)
    {
        switch (line.Name)
        {
            case "Position" when line.Arguments.Count >= 4 && EngineArgs.IsString(line.Arguments[3]) &&
                                 EngineArgs.TranslateString(EngineArgs.RelativeModeNames, 4, line.Arguments[3].Raw) < 0:
                add(ProblemLevel.Warning, block, line, $"\"{line.Arguments[3].Raw}\" names no unit: the game reads this Position as Pixels.");
                break;
            case "Rect" when line.Arguments.Count >= 5 && EngineArgs.IsString(line.Arguments[4]) &&
                             EngineArgs.TranslateString(EngineArgs.RelativeModeNames, 3, line.Arguments[4].Raw) < 0:
                add(ProblemLevel.Warning, block, line, $"\"{line.Arguments[4].Raw}\" names no unit a Rect takes (Pixels, Screen, Viewport): the game keeps Viewport.");
                break;
            case "BitmapRect" when line.Arguments.Count > 5:
                add(ProblemLevel.Warning, block, line, "The game reads a BitmapRect's unit only when it has exactly five arguments: this one is read as Viewport.");
                break;
            case "BitmapRect" when line.Arguments.Count == 5 && EngineArgs.IsString(line.Arguments[4]) &&
                                   EngineArgs.TranslateString(EngineArgs.RelativeModeNames, 3, line.Arguments[4].Raw) < 0:
                add(ProblemLevel.Warning, block, line, $"\"{line.Arguments[4].Raw}\" names no unit a BitmapRect takes (Pixels, Screen, Viewport): the game reads it as Viewport.");
                break;
        }
    }

    // ---- What each kind of element reads its own way -----------------------------------------------------------

    private static void Element(HudBlock block, ElementKind kind, HudBlock? parent, Context context, Report add)
    {
        var lines = block.Children;
        if (parent is not null && !IsViewPort(parent) && LastLine(block, "Viewport") is { } viewport &&
            (int)EngineArgs.FloatAt(viewport, 0) != context.ViewportOf(block))
            add(ProblemLevel.Warning, block, viewport,
                $"Viewport counts only on an element at the top: this one shows in its group's, {Shown(context.ViewportOf(block))}.");

        Events(block, kind, context, add);

        switch (kind)
        {
            case ElementKind.Model3D:
                Model(block, context, add);
                break;
            case ElementKind.VehicleSeating when LastLine(block, "Position") is { } position &&
                                                 EngineArgs.PositionMode(EngineArgs.At(position, 3)) != RelativeMode.Pixels:
                add(ProblemLevel.Warning, block, position,
                    "A VehicleSeating reads its own Position as pixels whatever its unit, and without its Rect's alignment.");
                break;
            case ElementKind.Map when LastLine(block, "Position") is { } position:
                add(ProblemLevel.Note, block, position, "A Map's own Position is replaced: it sits at its PositionSmall, PositionLarge or PositionSpawn.");
                break;
            case ElementKind.Target:
                foreach (var template in lines.Where(c => c.HasBody && !ElementKinds.TryGet(c, out _)))
                    foreach (var line in template.Children.Where(c => !c.HasBody && c.Name is "Position" or "Rotation" or "Scale"))
                        add(ProblemLevel.Note, template, line, $"A Target places, turns and scales each marker itself: its templates' {line.Name} is replaced.");
                break;
            case ElementKind.MultilineText:
                var numLines = LastLine(block, "NumLines");
                if (numLines is null || EngineArgs.FloatAt(numLines, 0) < 1)
                    add(ProblemLevel.Warning, block, numLines, "NumLines is 0" + (numLines is null ? " when it is not written" : string.Empty) + ": nothing shows.");
                foreach (var format in lines.Where(c => c.HasBody && c.Name == "Format"))
                    foreach (var line in format.Children.Where(c => !c.HasBody && c.Name is "TextBox" or "Position"))
                        add(ProblemLevel.Note, format, line, $"A MultilineText lays each line out itself: its Format's {line.Name} is replaced.");
                break;
            case ElementKind.ObjectiveList when !ObjectiveListName().IsMatch(NameOf(block) ?? string.Empty):
                add(ProblemLevel.Warning, block, null, "The game fills an ObjectiveList only when it is named playerNobjectives, such as player1objectives.");
                break;
        }

        if (ElementKinds.IsGroupBased(kind))
        {
            // RedGroupElement::SetBlendMode passes the mode to the children the group holds when the line is read.
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].HasBody || lines[i].Name != "BlendMode") continue;
                var before = lines.Take(i).Count(c => ElementKinds.TryGet(c, out _));
                var after = lines.Skip(i + 1).Count(c => ElementKinds.TryGet(c, out _));
                if (before == 0 && after > 0)
                    add(ProblemLevel.Warning, block, lines[i], "A group passes its BlendMode only to the children it holds when the line is read: none come before this line, so it changes nothing.");
                else if (before > 0 && after > 0)
                    add(ProblemLevel.Note, block, lines[i], $"A group passes its BlendMode only to the children it holds when the line is read: the {before} before this line, not the {after} after it.");
            }
        }
    }

    /// <summary>A FileInfo that keeps its file from loading, or loads it only some of the time.</summary>
    private static void FileInfo(HudBlock block, Report add)
    {
        if (LastLine(block, "SplitMode") is { } split && split.Arguments.Count > 0 &&
            !split.Arguments.Take(2).Any(a => EngineArgs.IsString(a) && EngineArgs.TranslateString(SplitModes, 2, a.Raw) == 1))
            add(ProblemLevel.Error, block, split, "The game loads a file only when its SplitMode lists \"Vertical\": this file never loads.");
        if (LastLine(block, "Widescreen") is { } wide && wide.Arguments.Count > 0)
            add(ProblemLevel.Note, block, wide, EngineArgs.Bool(wide.Arguments[0])
                ? "The game loads this file only with its widescreen setting on (modtools always has it on)."
                : "The game loads this file only with its widescreen setting off, which modtools never has.");
    }

    private static readonly string[] SplitModes = { "Horizontal", "Vertical" };

    private static void Model(HudBlock block, Context context, Report add)
    {
        var lines = block.Children;
        var mesh = IndexOf(block, "Mesh");
        if (mesh >= 0)
            foreach (var line in lines.Take(mesh).Where(c => !c.HasBody && c.Name is "Position" or "Rotation" or "Depth"))
                add(ProblemLevel.Note, block, line, $"Written before the Mesh line: if the model is loaded already, the game resets this {line.Name} as it reads the Mesh. The stock files write Mesh first.");
        if (LastLine(block, "BlendMode") is { } blend)
            add(ProblemLevel.Note, block, blend, "A Model3D keeps no blend mode: the game ignores its BlendMode.");

        if (LastLine(block, "InheritMeshInfo") is not { } inherit || StringAt(inherit, 0) is not { } name) return;
        // The table of a Model3D made before it, the first of that name.
        var from = context.Named.TryGetValue(name, out var list)
            ? list.Select(x => x.Block).FirstOrDefault(b => b.Name == "Model3D" && context.Before(b, block))
            : null;
        if (from is null)
        {
            add(ProblemLevel.Warning, block, inherit, $"No Model3D named \"{name}\" is made before this one: there is no table to share.");
            return;
        }
        var converted = from.Children.Where(c => c.HasBody && c.Name == "MeshInfo")
            .SelectMany(m => m.Children.Where(c => !c.HasBody && c.Name == "Position"))
            .Count(p => EngineArgs.PositionMode(EngineArgs.At(p, 3)) != RelativeMode.Pixels);
        if (converted > 0)
            add(ProblemLevel.Warning, block, inherit,
                $"The table of \"{name}\" has {converted} position{(converted == 1 ? "" : "s")} in a unit other than Pixels: each model that reads a table converts them again, so here they are multiplied by the view's size twice.");
    }

    // ---- Events, names, assets ----------------------------------------------------------------------------

    /// <summary>An event the element reads that nothing in the HUD sends: the game, GameExt or a transform.</summary>
    private static void Events(HudBlock block, ElementKind kind, Context context, Report add)
    {
        if (context.Unsent.Count == 0) return;
        foreach (var (property, name) in ElementReader.Read(block, kind).Bindings)
        {
            if (!context.Unsent.Contains(name)) continue;
            var line = block.Children.FirstOrDefault(c => !c.HasBody && c.Name == property && StringAt(c, 0) is { } n &&
                                                          string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            var sender = "Nothing here sends it: not the game, GameExt or a transform in these files";
            if (property == "EventEnable")
                add(ProblemLevel.Warning, block, line, $"\"{name}\" shows it, but {sender[0..1].ToLowerInvariant()}{sender[1..]}. It stays hidden unless a script sends it.");
            else
                add(ProblemLevel.Note, block, line, $"{property} \"{name}\": {sender}.");
        }
    }

    private static void Names(HudBlock block, HudDocument document, Context context, Report add)
    {
        if (NameOf(block) is not { Length: > 0 } name || !context.Named.TryGetValue(name, out var list) || list.Count < 2) return;
        // A copy for another player's view, as the split-screen files keep, is not a clash.
        var viewport = context.ViewportOf(block);
        var first = list.FirstOrDefault(x => context.ViewportOf(x.Block) is var v && (v == 0 || viewport == 0 || v == viewport));
        if (first.Block is null || ReferenceEquals(first.Block, block)) return;
        var where = ReferenceEquals(first.Document, document) ? string.Empty
            : $" in {Path.GetFileName(first.Document.SourcePath) ?? "another file"}";
        add(ProblemLevel.Note, block, null, $"{first.Block.Name} \"{NameOf(first.Block)}\"{where} has this name too: what finds an element by name (InheritMeshInfo, TransformNameMesh) finds that one.");
    }

    private static void Assets(HudBlock block, HudBlock line, Context context, Report add)
    {
        var (kind, name) = line.Name switch
        {
            "Bitmap" or "Mask" => (HudAssetKind.Texture, StringAt(line, 0)),
            "Mesh" => (HudAssetKind.Mesh, StringAt(line, 0)),
            "NameMesh" => (HudAssetKind.Mesh, StringAt(line, 1)),
            "TextFont" => (HudAssetKind.Font, StringAt(line, 0)),
            _ => (HudAssetKind.Texture, null),
        };
        if (Missing(context, kind, name))
            add(ProblemLevel.Warning, block, line, $"{kind} \"{name}\" is not found in the HUD's folders, the stock ones or an asset folder.");
    }

    private static bool Missing(Context context, HudAssetKind kind, string? name) =>
        name is { Length: > 0 } && context.Missing is { } missing && missing(kind, name);

    // ---- Helpers ------------------------------------------------------------------------------------------

    /// <summary>An element or a transform: what a name finds. Templates are not.</summary>
    private static bool IsNamedItem(HudBlock block) =>
        ElementKinds.TryGet(block, out _) || block.HasBody && block.Name.StartsWith("Transform", StringComparison.Ordinal);

    private static bool IsViewPort(HudBlock block) => block.Name.Equals("ViewPort", StringComparison.OrdinalIgnoreCase);

    private static string Shown(int viewport) => viewport == 0 ? "every viewport" : $"viewport {viewport}";

    /// <summary>An event name for any player: player1. to player4. and a ViewPort's player%. alike.</summary>
    private static string AnyPlayer(string name) => PlayerPrefix().Replace(name, "player%.");

    [GeneratedRegex(@"^player[1-4%]\.", RegexOptions.IgnoreCase)]
    private static partial Regex PlayerPrefix();

    private static string? NameOf(HudBlock block) => StringAt(block, 0);

    private static string? StringAt(HudBlock block, int index) =>
        index < block.Arguments.Count && EngineArgs.IsString(block.Arguments[index]) ? block.Arguments[index].Raw : null;

    private static HudBlock? LastLine(HudBlock block, string name) =>
        block.Children.LastOrDefault(c => !c.HasBody && c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static int IndexOf(HudBlock block, string name)
    {
        for (var i = 0; i < block.Children.Count; i++)
            if (!block.Children[i].HasBody && block.Children[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static string Describe(HudBlock block) =>
        IsNamedItem(block) && NameOf(block) is { } name ? $"{block.Name} \"{name}\"" : $"A {block.Name}";

    private static IEnumerable<HudBlock> Blocks(IEnumerable<HudBlock> blocks)
    {
        foreach (var b in blocks)
        {
            yield return b;
            foreach (var inner in Blocks(b.Children)) yield return inner;
        }
    }

    [GeneratedRegex(@"^player\d+objectives$", RegexOptions.IgnoreCase)]
    private static partial Regex ObjectiveListName();
}
