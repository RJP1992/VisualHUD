namespace SwbfHudEditor.Core.Hud;

/// <summary>Factory for new HudBlock instances with sensible default properties for each element type.</summary>
public static class HudBlockTemplates
{
    public static HudBlock NewGroup(string name) => Build("Group", name, hasBody: true,
        Leaf("Position", Float(0.5), Float(0.5), Float(0.0), Str("Viewport")),
        Leaf("ZOrder", Int(0)));

    public static HudBlock NewBitmap(string name) => Build("Bitmap", name, hasBody: true,
        Leaf("Bitmap", Str("hud_white")),
        Leaf("BitmapRect", Float(0.1), Float(0.1), Str("Left"), Str("Top"), Str("Viewport")),
        Leaf("Position", Float(0.5), Float(0.5), Float(0.0), Str("Viewport")),
        Leaf("Color", Int(255), Int(255), Int(255)),
        Leaf("EventEnable", Str("initialize")));

    public static HudBlock NewBarBitmap(string name) => Build("BarBitmap", name, hasBody: true,
        Leaf("Bitmap", Str("hud_white")),
        Leaf("BitmapRect", Float(0.2), Float(0.02), Str("Left"), Str("Top"), Str("Viewport")),
        Leaf("Position", Float(0.5), Float(0.5), Float(0.0), Str("Viewport")),
        Leaf("Scale", Float(1.0), Float(1.0), Float(1.0)),
        Leaf("Color", Int(255), Int(255), Int(255)),
        Leaf("EventValue", Str("player1.healthFraction")),
        Leaf("EventEnable", Str("initialize")));

    public static HudBlock NewText(string name) => Build("Text", name, hasBody: true,
        Leaf("TextBox", Float(0.2), Float(0.05)),
        Leaf("TextFont", Str("gamefont_small")),
        Leaf("Position", Float(0.5), Float(0.5), Float(0.0), Str("Viewport")),
        Leaf("Color", Int(255), Int(255), Int(255)),
        Leaf("EventEnable", Str("initialize")));

    public static HudBlock NewModel3D(string name) => Build("Model3D", name, hasBody: true,
        Leaf("Mesh", Str("hud_infinite_ammo_icon")),
        Leaf("Position", Float(0.5), Float(0.5), Float(0.0), Str("Viewport")),
        Leaf("Scale", Float(1.0), Float(1.0), Float(1.0)),
        Leaf("ZOrder", Int(0)),
        Leaf("EventEnable", Str("initialize")));

    private static HudBlock Build(string type, string name, bool hasBody, params HudBlock[] children)
    {
        var b = new HudBlock { Name = type, HasBody = hasBody };
        b.Arguments.Add(new HudArgument(HudArgKind.String, name));
        foreach (var c in children) b.Children.Add(c);
        return b;
    }

    private static HudBlock Leaf(string name, params HudArgument[] args)
    {
        var b = new HudBlock { Name = name, HasBody = false };
        foreach (var a in args) b.Arguments.Add(a);
        return b;
    }

    private static HudArgument Str(string s) => new(HudArgKind.String, s);
    private static HudArgument Int(long n) => HudArgument.FromInt(n);
    private static HudArgument Float(double n) => HudArgument.FromFloat(n);
}
