using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using SwbfHudEditor.App.Views;
using SwbfHudEditor.Core.Assets;
using SwbfHudEditor.Core.Events;
using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.App;

/// <summary>
/// Renders a <c>.hud</c> file as the editor's canvas draws it to a PNG, without a window:
/// <code>
/// VisualHUD.exe --snapshot file.hud out.png [--add other.hud]... [--screen 1280x720] [--game gameext|vanilla]
///               [--snap on|off] [--viewport 1] [--background shot.png] [--no-placeholders] [--no-guides]
///               [--preset onfoot] [--send event[=value]]... [--show events|all] [--dump layout.txt]
///               [--text examples|long|keys]
/// </code>
/// <c>--add</c>, repeated, loads more files after the first, as the game loads a HUD's files: each laid
/// out by its own <c>FileInfo</c>. The transform files beside them load first, as in the editor.
/// Assets are found as in the editor: the files' mod data folders and the stock data folder are
/// searched automatically, then its saved asset folders; <c>--assets folder</c>, repeated, searches more.
/// <c>--background</c> draws the HUD over an image of the same size, such as an in-game screenshot.
/// <c>--preset onfoot</c> sends the Events tab's on-foot scenario, then <c>--send</c>, repeated, sends
/// more events in that order (a value typed as in the Events tab). They decide what shows, as in the
/// game, unless <c>--show all</c> keeps every element shown and lets them set only what they set.
/// <c>--dump</c> writes every element as laid out: shown or not, where, and what it draws.
/// Errors go to <c>out.png.error.txt</c>; the exit code is 0 on success.
/// </summary>
internal static class Snapshot
{
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        var at = Array.IndexOf(args, "--snapshot");
        if (at < 0) return false;
        var output = at + 2 < args.Length ? args[at + 2] : "snapshot.png";
        try
        {
            Run(args, at);
        }
        catch (Exception ex)
        {
            File.WriteAllText(output + ".error.txt", ex.ToString());
            exitCode = 1;
        }
        return true;
    }

    private static void Run(string[] args, int at)
    {
        if (at + 2 >= args.Length) throw new ArgumentException("--snapshot takes a .hud file and an output .png");
        var hudPath = Path.GetFullPath(args[at + 1]);
        var output = Path.GetFullPath(args[at + 2]);

        var screen = new HudScreen(1280, 720);
        var target = HudTarget.GameExt;
        bool? snap = null;
        var viewport = 1;
        var placeholders = true;
        var guides = true;
        string? background = null;
        var sends = new List<(string Name, string? Value)>();
        string? dump = null;
        string? preset = null;
        var showByEvents = true;
        var textPreview = TextPreview.Examples;
        var paths = new List<string> { hudPath };
        var assetFolders = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--screen":
                    var parts = Next().ToLowerInvariant().Split('x');
                    screen = new HudScreen(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
                    break;
                case "--game":
                    target = Next().ToLowerInvariant() == "vanilla" ? HudTarget.Vanilla : HudTarget.GameExt;
                    break;
                case "--snap":
                    snap = Next().ToLowerInvariant() is "on" or "1" or "true";
                    break;
                case "--viewport":
                    viewport = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--background":
                    background = Path.GetFullPath(Next());
                    break;
                case "--no-placeholders":
                    placeholders = false;
                    break;
                case "--no-guides":
                    guides = false;
                    break;
                case "--send":
                    var send = Next();
                    var eq = send.IndexOf('=');
                    sends.Add(eq < 0 ? (send, null) : (send[..eq], send[(eq + 1)..]));
                    break;
                case "--dump":
                    dump = Path.GetFullPath(Next());
                    break;
                case "--add":
                    paths.Add(Path.GetFullPath(Next()));
                    break;
                case "--text":
                    textPreview = Next().ToLowerInvariant() switch
                    {
                        "long" => TextPreview.Long,
                        "keys" => TextPreview.Keys,
                        "examples" => TextPreview.Examples,
                        _ => throw new ArgumentException("--text takes examples, long or keys"),
                    };
                    break;
                case "--assets":
                    assetFolders.Add(Path.GetFullPath(Next()));
                    break;
                case "--show":
                    showByEvents = Next().ToLowerInvariant() switch
                    {
                        "events" => true,
                        "all" => false,
                        _ => throw new ArgumentException("--show takes events or all"),
                    };
                    break;
                case "--preset":
                    preset = Next().ToLowerInvariant();
                    if (preset != "onfoot") throw new ArgumentException("--preset takes onfoot");
                    break;
            }
        }

        var docs = HudFileSet.Load(paths);
        var assets = MainWindow.ResolverFor(paths, MainWindow.AutomaticAssetFoldersFor(paths)
            .Concat(AppSettings.Load().AssetFolders).Concat(assetFolders)
            .Distinct(HudFileSet.PathComparer).Select(folder => AssetFolderIndex.Scan(folder)).ToList());
        var canvas = new HudCanvas();
        canvas.Configure(target, screen, snap, viewport, placeholders, guides);
        canvas.LoadDocuments(docs, assets, MainWindow.TryLoadLocalization(assets));
        canvas.TextPreview = textPreview;
        // The guides are the first file's: the files come after their transforms.
        canvas.ActiveDocument = docs[docs.Count - paths.Count];
        if (sends.Count > 0 || preset is not null)
        {
            // Type each event by what it is, else by how the files use it.
            var uses = HudEventUsage.Of(docs, canvas.Transforms!).ToDictionary(u => u.Name, StringComparer.OrdinalIgnoreCase);
            var state = new HudEventState();
            if (preset == "onfoot") state.SendAll(HudEventPresets.OnFoot(canvas.Transforms!));
            foreach (var (name, text) in sends)
            {
                var type = HudEventCatalog.Find(name)?.Type ?? (uses.TryGetValue(name, out var use) ? use.Type : HudEventType.Bool);
                if (!HudEventText.TryParse(type, text ?? HudEventText.Format(HudEventValue.Default(type)), out var value))
                    throw new ArgumentException($"{name}: \"{text}\" is not a {type}");
                state.Send(name, value);
            }
            canvas.ShowByEvents = showByEvents;
            canvas.SetEventState(state);
        }

        BitmapSource? backdrop = null;
        if (background is not null)
        {
            var decoder = BitmapDecoder.Create(new Uri(background), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            backdrop = decoder.Frames[0];
        }
        var image = canvas.RenderSnapshot(backdrop);
        if (dump is not null) File.WriteAllText(dump, canvas.DescribeLayout());
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(output);
        encoder.Save(stream);
    }
}
