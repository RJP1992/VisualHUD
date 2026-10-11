using System.IO;
using SwbfHudEditor.App.Views;

namespace SwbfHudEditor.App.Tests;

/// <summary>The mod tools' folder: anything picked in it or above it is taken back to it, and the choice is kept.</summary>
public class ModToolsTests
{
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        """;

    /// <summary>A BF2_ModTools folder with the stock data and a mod's, in a scratch folder.</summary>
    private static (string Root, string Tools) MakeTools()
    {
        var root = Directory.CreateTempSubdirectory("visualhud-modtools-").FullName;
        var tools = Path.Combine(root, "BF2_ModTools");
        Directory.CreateDirectory(Path.Combine(tools, "data", "Common", "hud"));
        Directory.CreateDirectory(Path.Combine(tools, "data_ABC", "Common", "hud"));
        Directory.CreateDirectory(Path.Combine(root, "elsewhere"));
        return (root, tools);
    }

    [Fact]
    public void A_Folder_Picked_In_Or_Above_The_Mod_Tools_Is_Taken_Back_To_Them()
    {
        var (root, tools) = MakeTools();
        try
        {
            Assert.Equal(tools, ModTools.Resolve(tools));
            Assert.Equal(tools, ModTools.Resolve(Path.Combine(tools, "data")));
            Assert.Equal(tools, ModTools.Resolve(Path.Combine(tools, "data_ABC", "Common", "hud")));
            Assert.Equal(tools, ModTools.Resolve(root));
            Assert.Equal(tools, ModTools.Resolve($"\"{tools}\""));
            Assert.Null(ModTools.Resolve(Path.Combine(root, "elsewhere")));
            Assert.Null(ModTools.Resolve(""));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Going_Without_Is_Kept_And_A_Folder_Gone_Is_Looked_For_Again()
    {
        var (root, tools) = MakeTools();
        try
        {
            Assert.Equal(tools, ModTools.Choose(tools));
            Assert.Null(ModTools.Choose(""));
            Assert.Equal(ModTools.Detect(), ModTools.Choose(Path.Combine(root, "elsewhere")));
            Assert.Equal(ModTools.Detect(), ModTools.Choose(null));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_Dialog_Takes_Only_A_Folder_With_The_Mod_Tools_In_It()
    {
        var (root, tools) = MakeTools();
        try
        {
            UiThread.WithWindow(Hud, (_, _) =>
            {
                var dialog = new ModToolsDialog(Path.Combine(tools, "data_ABC"), firstStart: true);
                Assert.Equal(tools, dialog.Folder);
                Assert.True(dialog.OkButton.IsEnabled);
                Assert.StartsWith("Welcome", dialog.IntroText.Text);

                dialog.FolderBox.Text = Path.Combine(root, "elsewhere");
                Assert.Null(dialog.Folder);
                Assert.False(dialog.OkButton.IsEnabled);
                Assert.Contains("BF2_ModTools folder itself", dialog.StatusText.Text);
                dialog.Close();
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
