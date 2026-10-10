using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Tests.Layout;

/// <summary>A group's BlendMode reaches the children it holds when the line is read, and replaces their own.</summary>
public class BlendModeTests
{
    private static LaidOutElement Named(HudLayoutResult r, string name) => r.All.Single(e => e.Piece is null && e.Spec.Name == name);

    [Fact]
    public void A_Groups_BlendMode_Reaches_Only_The_Children_Read_Before_It()
    {
        var r = HudLayout.Lay(HudParser.Parse("""
            Group("g")
            {
                Bitmap("before") { BlendMode("Alpha") }
                BlendMode("Additive")
                Bitmap("after") { }
                Group("inner")
                {
                    Bitmap("deep") { }
                    BlendMode("Alpha")
                }
                Model3D("model") { }
            }
            Group("outer")
            {
                Group("mid") { Bitmap("own") { BlendMode("Additive") } }
            }
            """), new LayoutOptions());

        Assert.True(Named(r, "before").Additive);      // the group's later line replaces its own Alpha
        Assert.False(Named(r, "after").Additive);      // read after the group's line
        Assert.False(Named(r, "deep").Additive);       // its group's later Alpha
        Assert.False(Named(r, "model").Additive);      // a model keeps none
        Assert.True(Named(r, "own").Additive);         // nothing above it says otherwise
    }

    [Fact]
    public void The_Outermost_Groups_Later_Line_Is_Read_Last_And_Wins()
    {
        var r = HudLayout.Lay(HudParser.Parse("""
            Group("outer")
            {
                Group("inner")
                {
                    Bitmap("b") { }
                    BlendMode("Additive")
                }
                BlendMode("Alpha")
            }
            """), new LayoutOptions());
        Assert.False(Named(r, "b").Additive);
    }
}
