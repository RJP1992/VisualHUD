namespace SwbfHudEditor.Core.Hud;

public static class HudBlockExtensions
{
    // Reading properties the way the game does is SwbfHudEditor.Core.Layout.ElementReader's job;
    // these helpers only walk the block tree.

    public static HudBlock? Find(this HudBlock block, string name)
    {
        foreach (var c in block.Children)
            if (c.Name == name) return c;
        return null;
    }

    public static IEnumerable<HudBlock> FindAll(this HudBlock block, string name)
    {
        foreach (var c in block.Children)
            if (c.Name == name) yield return c;
    }

    /// <summary>
    /// Find the container (TopLevel or a block's Children) that holds <paramref name="target"/>,
    /// plus the index within that container and the target's parent block (null if top-level).
    /// Returns null if the target isn't in the document.
    /// </summary>
    public static (System.Collections.ObjectModel.ObservableCollection<HudBlock> Container, int Index, HudBlock? Parent)? FindContainer(this HudDocument doc, HudBlock target)
    {
        var topIdx = doc.TopLevel.IndexOf(target);
        if (topIdx >= 0) return (doc.TopLevel, topIdx, null);
        foreach (var top in doc.TopLevel)
        {
            var result = FindContainerRec(top, target);
            if (result is not null) return result;
        }
        return null;
    }

    private static (System.Collections.ObjectModel.ObservableCollection<HudBlock>, int, HudBlock?)? FindContainerRec(HudBlock current, HudBlock target)
    {
        var idx = current.Children.IndexOf(target);
        if (idx >= 0) return (current.Children, idx, current);
        foreach (var child in current.Children)
        {
            var deeper = FindContainerRec(child, target);
            if (deeper is not null) return deeper;
        }
        return null;
    }

    public static IReadOnlyList<HudBlock>? FindPathTo(this HudDocument doc, HudBlock target)
    {
        foreach (var top in doc.TopLevel)
        {
            var path = new List<HudBlock>();
            if (FindPath(top, target, path)) return path;
        }
        return null;
    }

    private static bool FindPath(HudBlock current, HudBlock target, List<HudBlock> path)
    {
        path.Add(current);
        if (ReferenceEquals(current, target)) return true;
        foreach (var c in current.Children)
            if (FindPath(c, target, path)) return true;
        path.RemoveAt(path.Count - 1);
        return false;
    }
}
