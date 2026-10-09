using System.Text;

namespace SwbfHudEditor.Core.Hud;

public static class HudWriter
{
    /// <summary>The document as text; lines <paramref name="omit"/> picks are left out, the document is not changed.</summary>
    public static string Write(HudDocument doc, string indent = "    ", Func<HudBlock, bool>? omit = null)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < doc.TopLevel.Count; i++)
        {
            WriteBlock(sb, doc.TopLevel[i], depth: 0, indent, omit);
            if (i < doc.TopLevel.Count - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    private static void WriteBlock(StringBuilder sb, HudBlock block, int depth, string indent, Func<HudBlock, bool>? omit)
    {
        AppendIndent(sb, depth, indent);
        sb.Append(block.Name).Append('(');
        for (var i = 0; i < block.Arguments.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            WriteArgument(sb, block.Arguments[i]);
        }
        sb.Append(')');

        if (block.HasBody)
        {
            sb.Append('\n');
            AppendIndent(sb, depth, indent);
            sb.Append("{\n");
            foreach (var child in block.Children)
            {
                if (omit?.Invoke(child) == true) continue;
                WriteBlock(sb, child, depth + 1, indent, omit);
                sb.Append('\n');
            }
            AppendIndent(sb, depth, indent);
            sb.Append('}');
        }
    }

    private static void WriteArgument(StringBuilder sb, HudArgument arg)
    {
        switch (arg.Kind)
        {
            case HudArgKind.String:
                sb.Append('"').Append(arg.Raw).Append('"');
                break;
            case HudArgKind.Integer:
            case HudArgKind.Float:
            case HudArgKind.Identifier:
                sb.Append(arg.Raw);
                break;
        }
    }

    private static void AppendIndent(StringBuilder sb, int depth, string indent)
    {
        for (var i = 0; i < depth; i++) sb.Append(indent);
    }
}
