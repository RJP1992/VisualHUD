using SwbfHudEditor.Core.Assets;

namespace SwbfHudEditor.Core.Tests.Assets;

public class TgaLoaderTests
{
    private const string ModtoolsInterface = @"C:\BF2_ModTools\data\Common\interface";

    [Theory]
    [InlineData("hud_health.tga")]
    [InlineData("hud_jetpack_and_energybar_fill.tga")]
    [InlineData("hud_objective_icon.tga")]
    public void Loads_Stock_Tga(string fileName)
    {
        var path = Path.Combine(ModtoolsInterface, fileName);
        if (!File.Exists(path)) return;

        var bmp = TgaLoader.Load(path);
        Assert.True(bmp.Width > 0);
        Assert.True(bmp.Height > 0);
        Assert.Equal(bmp.Width * bmp.Height * 4, bmp.Bgra32Data.Length);
    }

    [Fact]
    public void AssetResolver_Finds_Texture_In_Search_Root()
    {
        if (!Directory.Exists(ModtoolsInterface)) return;

        var resolver = new AssetResolver();
        resolver.AddTextureRoot(ModtoolsInterface);
        var found = resolver.FindTexture("hud_health");
        Assert.NotNull(found);
        Assert.True(File.Exists(found));
    }

    [Fact]
    public void AssetResolver_ForHudFile_Wires_Sibling_Interface_Folder()
    {
        var hudPath = @"C:\BF2_ModTools\data\Common\hud\1playerhud.hud";
        if (!File.Exists(hudPath)) return;

        var resolver = AssetResolver.ForHudFile(hudPath);
        Assert.Contains(resolver.TextureRoots, p => p.EndsWith(@"Common\interface", StringComparison.OrdinalIgnoreCase));
    }
}
