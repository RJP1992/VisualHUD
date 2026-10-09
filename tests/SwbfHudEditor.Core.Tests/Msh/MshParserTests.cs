using SwbfHudEditor.Core.Msh;

namespace SwbfHudEditor.Core.Tests.Msh;

public class MshParserTests
{
    private const string ModtoolsMshFolder = @"C:\BF2_ModTools\data\Common\mshs";

    [Theory]
    [InlineData("hud_infinite_ammo_icon.msh")]
    [InlineData("hud_health_regen.msh")]
    public void Extracts_At_Least_One_Texture_Name(string fileName)
    {
        var path = Path.Combine(ModtoolsMshFolder, fileName);
        if (!File.Exists(path)) return;

        var names = MshParser.ExtractTextureNames(path);
        Assert.NotEmpty(names);
        Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
    }
}
