using System.Text.RegularExpressions;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Hud;

/// <summary>
/// Blocks as <c>.hud</c> text and back, for Copy, Cut, Paste and Duplicate: what is copied pastes into
/// a text editor as written, and text from one pastes in as blocks.
/// </summary>
public static partial class HudClipboard
{
    /// <summary>The blocks as <c>.hud</c> text, in the writer's own layout, their comments kept.</summary>
    public static string Write(IEnumerable<HudBlock> blocks, string indent = "    ", string newline = "\r\n")
    {
        var doc = new HudDocument { Indent = indent, LineEnding = newline, FinalLineEnding = newline };
        foreach (var block in blocks) doc.TopLevel.Add(Clone(block));
        return HudWriter.Write(doc);
    }

    /// <summary>The blocks in <c>.hud</c> text, or null when it is not <c>.hud</c> text.</summary>
    public static IReadOnlyList<HudBlock>? Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var doc = HudParser.Parse(text);
            if (doc.TopLevel.Count == 0) return null;
            foreach (var block in doc.TopLevel) ForgetLayout(block);
            return doc.TopLevel.ToList();
        }
        catch (HudParseException)
        {
            return null;
        }
    }

    /// <summary>
    /// A deep copy of a block, to be put somewhere else: its comments go with it, its indentation and
    /// the spelling it was read with do not, so it is written in the place it goes to.
    /// </summary>
    public static HudBlock Clone(HudBlock block)
    {
        var copy = new HudBlock { Name = block.Name, HasBody = block.HasBody, Trivia = CloneTrivia(block.Trivia) };
        copy.Arguments.AddRange(block.Arguments);
        foreach (var child in block.Children) copy.Children.Add(Clone(child));
        return copy;
    }

    private static HudTrivia? CloneTrivia(HudTrivia? trivia)
    {
        if (trivia is null) return null;
        var copy = new HudTrivia
        {
            Before = trivia.Before is { } before ? new List<string>(before) : null,
            After = trivia.After,
            AfterOpen = trivia.AfterOpen,
            BeforeClose = trivia.BeforeClose is { } beforeClose ? new List<string>(beforeClose) : null,
            AfterClose = trivia.AfterClose,
            Reindent = true,
        };
        return copy.Before is null && copy.After is null && copy.AfterOpen is null && copy.BeforeClose is null && copy.AfterClose is null
            ? null
            : copy;
    }

    private static void ForgetLayout(HudBlock block)
    {
        if (block.Trivia is { } trivia)
        {
            trivia.Indent = null;
            trivia.OpenIndent = null;
            trivia.CloseIndent = null;
            trivia.RawHeader = null;
            trivia.HeaderAsRead = null;
            trivia.Reindent = true;
        }
        foreach (var child in block.Children) ForgetLayout(child);
    }

    /// <summary>
    /// Give every element and transform among <paramref name="blocks"/>, and in them, a name no block of
    /// the HUD has (<paramref name="nameTaken"/>), and every event a pasted transform sends a name none
    /// sends yet (<paramref name="eventTaken"/>): GameExt's transforms refuse an output that exists. A
    /// template keeps its name, as the game finds it by keyword. Returns what was renamed.
    /// </summary>
    public static IReadOnlyList<(string From, string To)> MakeNamesUnique(IEnumerable<HudBlock> blocks,
        Func<string, bool> nameTaken, Func<string, bool> eventTaken)
    {
        var renamed = new List<(string, string)>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(HudBlock block)
        {
            var isTransform = block.HasBody && block.Name.StartsWith("Transform", StringComparison.Ordinal);
            if (block.HasBody && (ElementKinds.TryGet(block, out _) || isTransform) &&
                block.Arguments.Count > 0 && block.Arguments[0].Kind == HudArgKind.String)
            {
                var name = block.Arguments[0].Raw;
                // An ObjectiveList is found by its name, playerNobjectives: it keeps it.
                if (block.Name != "ObjectiveList")
                {
                    var unique = Unique(name, n => nameTaken(n) || usedNames.Contains(n));
                    if (unique != name)
                    {
                        block.Arguments[0] = HudArgument.FromString(unique);
                        renamed.Add((name, unique));
                    }
                    usedNames.Add(unique);
                }
            }
            if (isTransform)
            {
                foreach (var line in block.Children)
                {
                    if (line.HasBody || line.Arguments.Count == 0 || line.Arguments[0].Kind != HudArgKind.String) continue;
                    if (line.Name is not ("EventOutput" or "EventOutputTrue" or "EventOutputFalse")) continue;
                    var output = line.Arguments[0].Raw;
                    var unique = Unique(output, e => eventTaken(e) || usedEvents.Contains(e));
                    if (unique != output)
                    {
                        line.Arguments[0] = HudArgument.FromString(unique);
                        renamed.Add((output, unique));
                    }
                    usedEvents.Add(unique);
                }
            }
            foreach (var child in block.Children) Visit(child);
        }
        foreach (var block in blocks) Visit(block);
        return renamed;
    }

    /// <summary><paramref name="name"/>, or its stem with the first free <c>_n</c> after it.</summary>
    public static string Unique(string name, Func<string, bool> taken)
    {
        if (!taken(name)) return name;
        var stem = NumberedSuffix().Replace(name, string.Empty);
        if (stem.Length == 0) stem = name;
        for (var i = 1; i < 1_000_000; i++)
        {
            var candidate = $"{stem}_{i}";
            if (!taken(candidate)) return candidate;
        }
        return name;
    }

    [GeneratedRegex(@"_\d+$")]
    private static partial Regex NumberedSuffix();
}
