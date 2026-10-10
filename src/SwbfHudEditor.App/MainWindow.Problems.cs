using SwbfHudEditor.App.Views;
using SwbfHudEditor.Core.Assets;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Problems;

namespace SwbfHudEditor.App;

/// <summary>The Problems tab: what the game reads otherwise than the files seem to say.</summary>
public partial class MainWindow
{
    private bool _problemListStale = true;

    private void SetUpProblems()
    {
        ProblemList.ProblemPicked += (_, problem) => ShowProblem(problem, edit: false);
        ProblemList.ProblemOpened += (_, problem) => ShowProblem(problem, edit: true);
    }

    /// <summary>Check the files opened again, now if the tab shows, else when it next does.</summary>
    private void RefreshProblemList()
    {
        _problemListStale = true;
        if (!ProblemsTab.IsSelected) return;
        _problemListStale = false;
        var opened = SearchedDocuments;
        if (opened.Count == 0)
        {
            ProblemList.Show(null);
            return;
        }
        // Each texture, model and font looked for once.
        Func<HudAssetKind, string, bool>? missing = null;
        if (_assets is { } assets)
        {
            var known = new Dictionary<(HudAssetKind, string), bool>();
            missing = (kind, name) =>
            {
                var key = (kind, name.ToLowerInvariant());
                if (!known.TryGetValue(key, out var gone)) known[key] = gone = assets.Find(kind, name) is null;
                return gone;
            };
        }
        var rows = HudProblems.Check(opened, Documents, missing).Select(p => new ProblemRow
        {
            Problem = p,
            Group = new ProblemGroup(p.Block,
                p.Block.ItemName.Length > 0 ? $"{p.Block.Name} \"{p.Block.ItemName}\"" : p.Block.Name,
                FileOf(p.Document)?.Name ?? string.Empty),
        }).ToList();
        ProblemList.Show(rows);
    }

    /// <summary>Show where a problem is: its block, and to edit, its line's box in the Properties panel.</summary>
    private void ShowProblem(HudProblem problem, bool edit)
    {
        if (Locate(problem.Block) is not { } at) return;
        ClearPicks();
        SelectPath(at.File, at.Path);
        if (!edit) return;
        if (problem.Line is null)
        {
            Properties.FocusName();
            return;
        }
        Properties.FlushRows();
        if (problem.Line.Arguments.Count == 0 || Properties.BoxFor(problem.Line, 0) is not { } box) return;
        box.Focus();
        box.SelectAll();
    }
}
