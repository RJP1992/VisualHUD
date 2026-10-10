using System.Windows;
using SwbfHudEditor.App.Views;
using SwbfHudEditor.Core.Edits;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.App;

/// <summary>Rewriting a file for GameExt's AuthoredRatio at a screen shape, keeping its look there.</summary>
public partial class MainWindow
{
    private void ScreenShape_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveFile is not { } file || file.AddedAutomatically)
        {
            StatusText.Text = "Select one of the HUD's files, or an element in it, to lay it out for a screen shape.";
            return;
        }
        var dialog = new ScreenShapeDialog(file.Name, Canvas.ScreenSize.Height, AuthoredShape(file.Document)) { Owner = this };
        if (dialog.ShowDialog() == true) RewriteForShape(file, dialog.RatioWidth, dialog.RatioHeight);
    }

    /// <summary>The shape a file's FileInfo says it was written for, as whole numbers, if any.</summary>
    private static (int Width, int Height)? AuthoredShape(HudDocument document)
    {
        var lines = document.TopLevel.Where(b => b.HasBody && b.Name == "FileInfo").SelectMany(b => b.Children).Where(l => !l.HasBody).ToList();
        if (lines.LastOrDefault(l => l.Name == "AuthoredRatio" && l.Arguments.Count == 2) is { } ratio &&
            long.TryParse(ratio.Arguments[0].Raw, out var w) && long.TryParse(ratio.Arguments[1].Raw, out var h) && w > 0 && h > 0)
            return ((int)w, (int)h);
        return lines.Any(l => l.Name == "TrueWidescreen" && l.Arguments.Count > 0 && l.Arguments[0].Raw != "0") ? (4, 3) : null;
    }

    /// <summary>Rewrite a file for AuthoredRatio(w, h), keeping its look at that shape, in one undo step.</summary>
    internal RatioMigration? RewriteForShape(HudFile file, int w, int h)
    {
        if (HudRatioMigration.WhyNot(file.Document, w, h) is { } why)
        {
            StatusText.Text = why;
            return null;
        }
        var result = HudRatioMigration.Migrate(Documents, file.Document, w, h, Canvas.ScreenSize.Height,
            Canvas.LayoutOptions ?? new LayoutOptions());
        _undo.Push(new CompositeEdit(result.Edits, $"lay out {file.Name} for {w}:{h}"));
        _knownEvents = null;
        AfterBlocksChanged(Tree.SelectedItem as HudBlock);
        var at = result.Reference;
        StatusText.Text = $"{file.Name} is laid out for {w}:{h}: {result.Lines} line{(result.Lines == 1 ? "" : "s")} rewritten, in one undo step. " +
                          $"It draws as before at {at.Width}×{at.Height}." +
                          (result.Notes.Count > 0 ? " " + string.Join(" ", result.Notes) : string.Empty);
        return result;
    }
}
