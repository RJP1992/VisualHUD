using System.Text.RegularExpressions;
using SwbfHudEditor.Core.Edits;

namespace SwbfHudEditor.Core.Hud;

/// <summary>Text to find in the arguments of the open files, and how.</summary>
/// <param name="Text">What to find; nothing is found for an empty text.</param>
/// <param name="MatchCase">Upper and lower case must match.</param>
/// <param name="WholeValue">The argument must be the text and nothing more; otherwise the text is found inside it.</param>
/// <param name="StringsOnly">Look in strings only (names, events, textures, text keys), not in numbers and bare words.</param>
public sealed record FindQuery(string Text, bool MatchCase = false, bool WholeValue = false, bool StringsOnly = true)
{
    public StringComparison Comparison => MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}

/// <summary>
/// An argument the text was found in: one of a line's, or of a block's own header, as an element's name.
/// <see cref="Element"/> is the block with a body it belongs to, the row the Elements tree shows for it;
/// <see cref="Count"/> is how many times the text is in it.
/// </summary>
public sealed record FindMatch(HudDocument Document, HudBlock Line, int Index, HudBlock? Element, int Count)
{
    public HudArgument Value => Line.Arguments[Index];
}

/// <summary>Find and replace across the arguments of a HUD's files: every change one undo step, none that would not read back.</summary>
public static class HudFind
{
    // What the lexer reads as a number and as a bare word.
    private static readonly Regex Number = new(@"^[-+]?(\d+(\.\d*)?|\.\d+)([eE][-+]?\d+)?$", RegexOptions.CultureInvariant);
    private static readonly Regex Word = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>Every argument of the documents the query finds, file by file, top to bottom.</summary>
    public static List<FindMatch> Find(IEnumerable<HudDocument> documents, FindQuery query)
    {
        var found = new List<FindMatch>();
        if (query.Text.Length == 0) return found;
        foreach (var document in documents) Walk(document, document.TopLevel, null, query, found);
        return found;
    }

    private static void Walk(HudDocument document, IEnumerable<HudBlock> blocks, HudBlock? element, FindQuery query, List<FindMatch> found)
    {
        foreach (var block in blocks)
        {
            var owner = block.HasBody ? block : element;
            for (var i = 0; i < block.Arguments.Count; i++)
            {
                var arg = block.Arguments[i];
                if (query.StringsOnly && arg.Kind != HudArgKind.String) continue;
                if (Occurrences(arg.Raw, query) is var count and > 0) found.Add(new FindMatch(document, block, i, owner, count));
            }
            if (block.Children.Count > 0) Walk(document, block.Children, owner, query, found);
        }
    }

    /// <summary>How many times the query's text is in the value: once at most for a whole value.</summary>
    public static int Occurrences(string value, FindQuery query)
    {
        if (query.Text.Length == 0) return 0;
        if (query.WholeValue) return string.Equals(value, query.Text, query.Comparison) ? 1 : 0;
        var count = 0;
        for (var at = value.IndexOf(query.Text, query.Comparison); at >= 0; at = value.IndexOf(query.Text, at + query.Text.Length, query.Comparison))
            count++;
        return count;
    }

    /// <summary>The value with the text replaced: all of it for a whole value, every time it is in it otherwise.</summary>
    public static string Replace(string value, FindQuery query, string replacement) =>
        query.WholeValue
            ? string.Equals(value, query.Text, query.Comparison) ? replacement : value
            : query.Text.Length == 0 ? value : value.Replace(query.Text, replacement, query.Comparison);

    /// <summary>
    /// The argument a new text makes in the place of <paramref name="old"/>, of the same kind: null when the
    /// file could not hold it there, as a string with a quote or a line break, a number that is not one, or a
    /// bare word that is not. A number with a point or an exponent is a float, as the parser reads it.
    /// </summary>
    public static HudArgument? Retype(HudArgument old, string text) => old.Kind switch
    {
        HudArgKind.String => text.IndexOfAny(new[] { '"', '\n', '\r' }) < 0 ? HudArgument.FromString(text) : null,
        HudArgKind.Identifier => Word.IsMatch(text) ? HudArgument.FromIdentifier(text) : null,
        _ => Number.IsMatch(text)
            ? new HudArgument(text.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0 ? HudArgKind.Float : HudArgKind.Integer, text)
            : null,
    };

    /// <summary>What replacing the text in one match gives, or null when it cannot be written there.</summary>
    public static HudArgument? Replaced(FindMatch match, FindQuery query, string replacement) =>
        Retype(match.Value, Replace(match.Value.Raw, query, replacement));

    /// <summary>
    /// The changes that replacing the text in these matches makes, and the matches left as they are because
    /// the result could not be written there.
    /// </summary>
    public static (List<ArgumentChange> Changes, List<FindMatch> Refused) Changes(IEnumerable<FindMatch> matches, FindQuery query, string replacement)
    {
        var changes = new List<ArgumentChange>();
        var refused = new List<FindMatch>();
        foreach (var match in matches)
        {
            var old = match.Value;
            if (Replaced(match, query, replacement) is not { } changed)
            {
                refused.Add(match);
                continue;
            }
            if (changed != old) changes.Add(new ArgumentChange(match.Line, match.Index, old, changed));
        }
        return (changes, refused);
    }
}
