using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SwbfHudEditor.Core.Assets;

namespace SwbfHudEditor.App.Views;

/// <summary>One asset in the Assets panel.</summary>
public sealed class AssetRow
{
    private static readonly Brush FoundBrush = new SolidColorBrush(Color.FromRgb(159, 212, 159));
    private static readonly Brush MissingBrush = new SolidColorBrush(Color.FromRgb(255, 138, 101));

    public required HudAsset Asset { get; init; }
    public string Kind => Asset.Kind.ToString();
    public string Name => Asset.Name;
    public string Where => Asset.Path ?? "missing";
    public Brush WhereBrush => Asset.Missing ? MissingBrush : FoundBrush;

    public string UsedBy => Asset.UsedBy.Count <= 2
        ? string.Join(", ", Asset.UsedBy)
        : $"{string.Join(", ", Asset.UsedBy.Take(2))} and {Asset.UsedBy.Count - 2} more";

    public string Details => $"{Asset.Kind} {Asset.Name}\n{Asset.Path ?? "Not found in the HUD's folders, the stock folders or an asset folder."}\n\nUsed by:\n" +
                             string.Join("\n", Asset.UsedBy.Take(30)) + (Asset.UsedBy.Count > 30 ? $"\n... and {Asset.UsedBy.Count - 30} more" : string.Empty);
}

/// <summary>An asset folder and what its scan found. An automatic one, searched for the open files, cannot be removed.</summary>
public sealed record AssetFolderRow(string Folder, string Status, bool Automatic)
{
    public Visibility RemoveVisibility => Automatic ? Visibility.Hidden : Visibility.Visible;
}

/// <summary>
/// The textures, models, fonts and localization the open HUD's files name, where each was found or
/// that it is missing, and the asset folders searched for the missing ones.
/// </summary>
public partial class AssetsPanel : UserControl
{
    private IReadOnlyList<HudAsset>? _assets;

    public event EventHandler? AddFolderRequested;
    public event EventHandler<string>? RemoveFolderRequested;
    public event EventHandler? RescanRequested;

    public AssetsPanel()
    {
        InitializeComponent();
    }

    public void ShowFolders(IReadOnlyList<AssetFolderRow> folders)
    {
        FolderList.ItemsSource = folders;
        NoFoldersText.Visibility = folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The HUD's assets, or null when no HUD is open.</summary>
    public void ShowAssets(IReadOnlyList<HudAsset>? assets)
    {
        _assets = assets;
        Fill();
    }

    private void Fill()
    {
        if (_assets is null)
        {
            SummaryText.Text = "Open a HUD to list its assets.";
            AssetList.ItemsSource = null;
            return;
        }
        var missing = _assets.Where(a => a.Missing).ToList();
        SummaryText.Text = missing.Count == 0
            ? $"All {_assets.Count} assets the HUD names were found."
            : $"{missing.Count} of {_assets.Count} assets missing: " +
              string.Join(", ", missing.GroupBy(a => a.Kind).Select(g => $"{g.Count()} {Plural(g.Key, g.Count())}")) + ".";
        var shown = MissingOnlyBox.IsChecked == true ? missing : _assets;
        AssetList.ItemsSource = shown.Select(a => new AssetRow { Asset = a }).ToList();
    }

    private static string Plural(HudAssetKind kind, int count) => kind switch
    {
        HudAssetKind.Texture => count == 1 ? "texture" : "textures",
        HudAssetKind.Mesh => count == 1 ? "mesh" : "meshes",
        HudAssetKind.Font => count == 1 ? "font" : "fonts",
        HudAssetKind.String => count == 1 ? "string" : "strings",
        _ => "localization",
    };

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        // The box's IsChecked fires this while the panel loads, before the list exists.
        if (AssetList is not null) Fill();
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e) => AddFolderRequested?.Invoke(this, EventArgs.Empty);

    private void Rescan_Click(object sender, RoutedEventArgs e) => RescanRequested?.Invoke(this, EventArgs.Empty);

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string folder }) RemoveFolderRequested?.Invoke(this, folder);
    }
}
