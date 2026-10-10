using SwbfHudEditor.Core.Events;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Tests.Hud;

/// <summary>Sums typed where a number goes.</summary>
public class NumberExpressionTests
{
    [Theory]
    [InlineData("5.12*2", 10.24)]
    [InlineData("128/4", 32)]
    [InlineData("(1920 - 64) / 2", 928)]
    [InlineData("-3+4*2", 5)]
    [InlineData("2*-3", -6)]
    [InlineData(" 7 ", 7)]
    [InlineData("1e-3*2", 0.002)]
    [InlineData("0.5", 0.5)]
    [InlineData(".25*4", 1)]
    public void Works_Out_A_Sum(string text, double expected)
    {
        Assert.True(NumberExpression.TryEvaluate(text, out var value));
        Assert.Equal(expected, value, 9);
    }

    [Theory]
    [InlineData("2*")]
    [InlineData("1/0")]
    [InlineData("abc")]
    [InlineData("(1+2")]
    [InlineData("")]
    [InlineData("4 4")]
    public void Refuses_What_Is_Not_One(string text) => Assert.False(NumberExpression.TryEvaluate(text, out _));

    [Fact]
    public void Event_Values_Take_Sums_And_A_Uint_Name_Stays_A_Name()
    {
        Assert.True(HudEventText.TryParse(HudEventType.Float, "100/4", out var f));
        Assert.Equal(25f, f.Float);
        Assert.True(HudEventText.TryParse(HudEventType.Int, "6*7", out var i));
        Assert.Equal(42, i.Int);
        Assert.True(HudEventText.TryParse(HudEventType.Uint, "2*3", out var u));
        Assert.Equal(6u, u.Uint);
        Assert.True(HudEventText.TryParse(HudEventType.Uint, "hud_icon-2", out var name));
        Assert.Equal("hud_icon-2", name.Text);
        Assert.True(HudEventText.TryParse(HudEventType.Vector3, "0.5, 1/4, 2-2", out var v));
        Assert.Equal(new System.Numerics.Vector3(0.5f, 0.25f, 0f), v.Vector);
    }
}
