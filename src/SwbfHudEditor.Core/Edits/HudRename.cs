using System.Text.RegularExpressions;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Edits;

/// <summary>A rename as one undoable edit, and what the user should know about it.</summary>
public sealed record RenamePlan(IHudEdit Edit, IReadOnlyList<string> Notes);

/// <summary>
/// Renaming a block: its name is its first argument, a string. The lines that find a block by its name
/// change with it: a Model3D's <c>InheritMeshInfo("name")</c> and a TransformNameMesh's
/// <c>TransformNameMesh("name")</c> line. The game hashes names without case.
/// </summary>
public static partial class HudRename
{
    /// <summary>The name a block goes by, or null when its first argument is not a string.</summary>
    public static string? NameOf(HudBlock block) =>
        block.Arguments.Count > 0 && block.Arguments[0].Kind == HudArgKind.String ? block.Arguments[0].Raw : null;

    /// <summary>Why <paramref name="block"/> cannot be renamed, or null. <paramref name="parent"/> is the block holding it.</summary>
    public static string? WhyNot(HudBlock block, HudBlock? parent)
    {
        if (!block.HasBody) return $"{block.Name} is a line: its arguments are edited in the Properties panel.";
        if (NameOf(block) is null) return $"This {block.Name} has no name.";
        // The map's, the targets', the seating chart's, a segmented bar's and a list's templates are
        // matched by their keyword (ElementMap::Read compares the line's own hash); the quoted name is a label.
        if (parent is not null && !ElementKinds.TryGet(block, out _) && ElementKinds.TryGet(parent, out var parentKind) &&
            parentKind is ElementKind.Map or ElementKind.Target or ElementKind.VehicleSeating or ElementKind.BarSegmented
                or ElementKind.MultilineText)
            return $"The game finds this template by its keyword, {block.Name}; the name in quotes is not used.";
        return null;
    }

    /// <summary>Why <paramref name="name"/> cannot be written as a name, or null.</summary>
    public static string? Invalid(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "A name cannot be empty.";
        if (name.Contains('"')) return "A name cannot hold a double quote.";
        if (name.Contains('\n') || name.Contains('\r')) return "A name cannot hold a line break.";
        return null;
    }

    /// <summary>
    /// The edit that renames <paramref name="block"/> to <paramref name="newName"/>, with the lines in
    /// <paramref name="documents"/> that named it, not yet applied; and notes on what the game makes of
    /// the new name.
    /// </summary>
    public static RenamePlan Plan(IReadOnlyList<HudDocument> documents, HudBlock block, string newName)
    {
        var oldName = NameOf(block) ?? throw new ArgumentException("The block has no name.", nameof(block));
        var edits = new List<IHudEdit>
        {
            new ReplaceArgumentEdit(block, 0, block.Arguments[0], HudArgument.FromString(newName)),
        };
        var notes = new List<string>();
        var isElement = ElementKinds.TryGet(block, out var kind);

        // The lines that find this block by name.
        var reference = isElement && kind == ElementKind.Model3D ? "InheritMeshInfo"
            : block.Name == "TransformNameMesh" ? "TransformNameMesh"
            : null;
        if (reference is not null && !Same(oldName, newName))
        {
            var lines = documents.SelectMany(d => Blocks(d.TopLevel))
                .Where(b => b.HasBody && b.Name == block.Name && !ReferenceEquals(b, block))
                .SelectMany(b => b.Children)
                .Where(l => !l.HasBody && l.Name == reference && NameOf(l) is { } named && Same(named, oldName))
                .ToList();
            foreach (var line in lines)
                edits.Add(new ReplaceArgumentEdit(line, 0, line.Arguments[0], HudArgument.FromString(newName), affected: block));
            if (lines.Count > 0)
                notes.Add($"{lines.Count} {reference} line{(lines.Count == 1 ? "" : "s")} that named it {(lines.Count == 1 ? "follows" : "follow")} it.");
        }

        if (!Same(oldName, newName))
        {
            var others = documents
                .SelectMany(d => Blocks(d.TopLevel).Select(b => (Block: b, Document: d)))
                .Where(x => !ReferenceEquals(x.Block, block) && x.Block.HasBody && IsNamedItem(x.Block) &&
                            NameOf(x.Block) is { } name && Same(name, newName))
                .ToList();
            if (others.Count > 0)
            {
                var (other, doc) = others[0];
                var where = Path.GetFileName(doc.SourcePath) is { Length: > 0 } file ? $" in {file}" : string.Empty;
                notes.Add($"{other.Name} \"{NameOf(other)}\"{where} has this name too" +
                          (others.Count > 1 ? $", and {others.Count - 1} more" : string.Empty) + ".");
            }
        }

        if (isElement && kind == ElementKind.ObjectiveList && !ObjectiveListName().IsMatch(newName))
            notes.Add("The game fills an ObjectiveList only when it is named playerNobjectives, such as player1objectives.");

        return new RenamePlan(new CompositeEdit(edits, $"rename {block.Name} \"{oldName}\" to \"{newName}\""), notes);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>An element or a transform: what a name can find. Templates and sub-scopes are not.</summary>
    private static bool IsNamedItem(HudBlock block) =>
        ElementKinds.TryGet(block, out _) || block.Name.StartsWith("Transform", StringComparison.Ordinal);

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
