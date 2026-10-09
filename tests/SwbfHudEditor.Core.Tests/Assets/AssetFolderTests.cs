using SwbfHudEditor.Core.Assets;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Tests.Assets;

/// <summary>Asset folders: scanned with all their subfolders, searched after a HUD's own folders.</summary>
public sealed class AssetFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "VisualHUD.Tests." + Guid.NewGuid().ToString("N"));

    public AssetFolderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* leave it for the temp cleaner */ }
    }

    private string Touch(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void A_Scan_Finds_Each_Kind_In_Every_Subfolder()
    {
        var texture = Touch(@"assets\deep\er\hud_a.TGA");
        var mesh = Touch(@"assets\mshs\hud_model.msh");
        var font = Touch(@"assets\Common\fonts\eurostile_medium.fff");
        var english = Touch(@"assets\Common\Localize\english.cfg");
        Touch(@"assets\Common\config\english.cfg");   // not in a Localize folder
        Touch(@"assets\notes.txt");
        var first = Touch(@"assets\a\dup.tga");
        Touch(@"assets\b\dup.tga");

        var index = AssetFolderIndex.Scan(Path.Combine(_root, "assets"));
        Assert.Equal(8, index.FileCount);
        Assert.Equal(texture, index.Textures["hud_a"]);
        Assert.Equal(mesh, index.Meshes["HUD_MODEL"]);
        Assert.Equal(font, index.Fonts["eurostile_medium"]);
        Assert.Equal(english, index.Localizations["english"]);
        Assert.Single(index.Localizations);
        Assert.Equal(first, index.Textures["dup"]);   // the first path in ordinal order, every time
    }

    [Fact]
    public void A_Folder_Fills_In_What_The_Hud_Folders_Lack()
    {
        // A mod laid out as the tools expect: data\Common\hud beside interface, mshs and fonts.
        var hud = Touch(@"mod\Common\hud\my.hud");
        var own = Touch(@"mod\Common\interface\shared.tga");
        var ownFont = Touch(@"mod\Common\fonts\gamefont_small.fff");
        Touch(@"extra\shared.tga");
        var onlyExtra = Touch(@"extra\textures\only_here.tga");
        var extraMesh = Touch(@"extra\models\only_here_model.msh");
        var extraFont = Touch(@"extra\fonts\eurostile_medium.fff");

        var resolver = AssetResolver.ForHudFile(hud);
        Assert.Null(resolver.FindTexture("only_here"));
        resolver.AddFolder(AssetFolderIndex.Scan(Path.Combine(_root, "extra")));

        Assert.Equal(own, resolver.FindTexture("shared"));          // the HUD's own copy wins
        Assert.Equal(onlyExtra, resolver.FindTexture("only_here"));
        Assert.Equal(extraMesh, resolver.FindMesh("only_here_model"));
        Assert.Equal(ownFont, resolver.FindFont("gamefont_small"));
        Assert.Equal(extraFont, resolver.FindFont("eurostile_medium"));
        Assert.Null(resolver.FindFont("gamefont_large"));
    }

    [Fact]
    public void The_Check_Lists_What_The_Files_Name()
    {
        var hudPath = Touch(@"mod\Common\hud\my.hud");
        Touch(@"mod\Common\interface\hud_found.tga");
        Touch(@"mod\Common\fonts\gamefont_small.fff");
        var doc = HudParser.Parse("""
            Group("g")
            {
                Bitmap("b") { Bitmap("hud_found") BitmapRect(4, 4) }
                Bitmap("m") { Bitmap("hud_missing") Mask("hud_mask") }
                Text("t") { TextFont("gamefont_small") Text("level.key") }
                Text("u") { TextFont("eurostile_medium") }
                Model3D("w") { Mesh("hud_weapon") MeshInfo("hud_other") { Position(0, 0, 0) } }
            }
            TransformNameMesh("player1weapon1") { NameMesh("rep_weap_rifle", "hud_rifle") }
            """, hudPath);

        var assets = HudAssets.Check(new[] { doc }, AssetResolver.ForHudFile(hudPath));
        string? Path(HudAssetKind kind, string name) => assets.Single(a => a.Kind == kind && a.Name == name).Path;

        Assert.NotNull(Path(HudAssetKind.Texture, "hud_found"));
        Assert.Null(Path(HudAssetKind.Texture, "hud_missing"));
        Assert.Null(Path(HudAssetKind.Texture, "hud_mask"));
        Assert.NotNull(Path(HudAssetKind.Font, "gamefont_small"));
        Assert.Null(Path(HudAssetKind.Font, "eurostile_medium"));
        Assert.Null(Path(HudAssetKind.Mesh, "hud_weapon"));
        Assert.Null(Path(HudAssetKind.Mesh, "hud_other"));
        Assert.Null(Path(HudAssetKind.Mesh, "hud_rifle"));
        Assert.Null(Path(HudAssetKind.Localization, "english"));
        // Element names are not assets.
        Assert.DoesNotContain(assets, a => a.Name is "b" or "m" or "t" or "w");
        Assert.Equal(new[] { "Bitmap \"m\" (my.hud)" }, assets.Single(a => a.Name == "hud_missing").UsedBy);
        Assert.Equal(new[] { "TransformNameMesh \"player1weapon1\" (my.hud)" }, assets.Single(a => a.Name == "hud_rifle").UsedBy);
    }
}
