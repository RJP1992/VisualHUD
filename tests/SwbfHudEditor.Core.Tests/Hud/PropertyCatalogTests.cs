using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Tests.Hud;

/// <summary>The Add tab offers what the game reads for a block, and only that.</summary>
public class PropertyCatalogTests
{
    private static HudBlock Parse(string source) => HudParser.Parse(source).TopLevel[0];

    private static List<string> Offered(HudBlock block, HudBlock? parent = null) =>
        PropertyCatalog.GetAddable(block, parent).Select(d => d.Name).ToList();

    [Theory]
    [InlineData("Group")]
    [InlineData("Text")]
    [InlineData("Bitmap")]
    [InlineData("BitmapMasked")]
    [InlineData("BarBitmap")]
    [InlineData("ProceduralBarBitmap")]
    [InlineData("BarSegmented")]
    [InlineData("Model3D")]
    [InlineData("MultilineText")]
    [InlineData("BorderedBox")]
    [InlineData("ObjectiveList")]
    [InlineData("Map")]
    [InlineData("Target")]
    [InlineData("VehicleSeating")]
    public void Every_Element_Takes_What_Every_Element_Reads(string keyword)
    {
        var offered = Offered(Parse($"{keyword}(\"e\") {{ }}"));
        foreach (var name in new[] { "Position", "Rotation", "Scale", "ZOrder", "Alpha", "Color", "EventEnable", "EventDisable", "FadeOutTime", "ScreenAnchor" })
            Assert.Contains(name, offered);
    }

    [Fact]
    public void Each_Class_Gets_Its_Own_Keys()
    {
        var group = Offered(Parse("Group(\"g\") { }"));
        Assert.Contains("Rect", group);
        Assert.Contains("EventPosition", group);
        Assert.Contains("PropagateAlpha", group);
        Assert.DoesNotContain("Bitmap", group);
        Assert.DoesNotContain("Mesh", group);

        var bitmap = Offered(Parse("Bitmap(\"b\") { }"));
        Assert.Contains("Bitmap", bitmap);
        Assert.Contains("BitmapStyle", bitmap);
        Assert.Contains("EventBitmap", bitmap);
        Assert.DoesNotContain("Rect", bitmap);
        Assert.DoesNotContain("EventAlpha", bitmap);   // groups only
        Assert.DoesNotContain("Mesh", bitmap);         // Model3D only

        var bar = Offered(Parse("BarBitmap(\"b\") { }"));
        Assert.Contains("Value", bar);
        Assert.Contains("FillFrom", bar);
        Assert.Contains("BitmapRect", bar);

        var segmented = Offered(Parse("BarSegmented(\"s\") { }"));
        Assert.Contains("NumSegments", segmented);
        Assert.Contains("EventValue", segmented);
        Assert.Contains("Rect", segmented);
        Assert.Contains("Segment", segmented);
        Assert.DoesNotContain("BitmapRect", segmented);

        var text = Offered(Parse("Text(\"t\") { Text(\"level.key\") }"));
        Assert.Contains("TextBreak", text);
        Assert.Contains("EventNumber", text);
        Assert.DoesNotContain("Text", text);           // it has one

        Assert.Contains("AuthoredRatio", Offered(Parse("FileInfo(\"f\") { }")));
    }

    [Fact]
    public void A_Subclass_Key_Is_Offered_Once_As_The_Subclass_Reads_It()
    {
        var defs = PropertyCatalog.GetAddable(Parse("MultilineText(\"m\") { }")).Where(d => d.Name == "EventColor").ToList();
        Assert.Single(defs);
        Assert.Contains("next line", defs[0].Description);
    }

    [Fact]
    public void Templates_Get_What_Their_Element_Reads_In_Them()
    {
        var model = Parse("Model3D(\"m\") { MeshInfo(\"a\") { } }");
        // A model can hold any number of MeshInfo blocks.
        Assert.Contains("MeshInfo", Offered(model));
        var meshInfo = model.Children[0];
        Assert.Equal(new[] { "Position", "Rotation", "Scale", "Depth" }, Offered(meshInfo, model).OrderBy(n => n switch
        {
            "Position" => 0, "Rotation" => 1, "Scale" => 2, _ => 3,
        }));

        var target = Parse("Target(\"t\") { ObjectiveOnScreen(\"ObjectiveOnScreen\") { } }");
        var template = Offered(target.Children[0], target);
        Assert.Contains("MinScale", template);
        Assert.Contains("BitmapRect", template);

        // Without its parent a template's class is unknown, and nothing is offered.
        Assert.Empty(Offered(meshInfo));
    }

    [Fact]
    public void A_Transform_Is_Offered_The_Keys_Its_Class_Reads()
    {
        var math = Offered(Parse("TransformNumberMath(\"m\") { Operation(\"Add\") EventInputA(\"x\") }"));
        Assert.Contains("ConstantB", math);
        Assert.Contains("EventInputB", math);
        Assert.Contains("Clamp", math);
        Assert.Contains("OutputIsAlpha", math);
        Assert.DoesNotContain("ConstantA", math);      // EventInputA gives A already; both would switch it off
        Assert.DoesNotContain("Operation", math);      // it has one
        Assert.DoesNotContain("InputFactor", math);    // the native transforms' key
        Assert.DoesNotContain("Position", math);       // not an element

        var lerp = Offered(Parse("TransformNumberLerp(\"l\") { }"));
        Assert.Contains("RiseTime", lerp);
        Assert.Contains("InputRange", lerp);
        Assert.DoesNotContain("Clamp", lerp);

        var compare = Offered(Parse("TransformNumberCompare(\"c\") { }"));
        Assert.Contains("Hysteresis", compare);
        Assert.Contains("EventOutputTrue", compare);
        Assert.Contains("Greater", PropertyCatalog.GetAddable(Parse("TransformNumberCompare(\"c\") { }")).Single(d => d.Name == "Operation").Description);

        var color = Offered(Parse("TransformNumberColor(\"c\") { NumberColor(0, 255, 0, 0) EventInput(\"x\") }"));
        Assert.Contains("NumberColor", color);         // another row of the table
        Assert.Contains("InputFactor", color);
        Assert.Contains("WrapInput", color);
        Assert.DoesNotContain("EventInput", color);
        Assert.DoesNotContain("NumberVector3", color);

        var blend = Offered(Parse("TransformNumberColorBlend(\"b\") { }"));
        Assert.Contains("EventBlend", blend);
        Assert.Contains("NumberColor", blend);
        Assert.DoesNotContain("InputFactor", blend);   // read, but its input handler never applies it

        var mesh = Offered(Parse("TransformNameMesh(\"n\") { NameMesh(\"a\", \"b\") }"));
        Assert.Contains("NameMesh", mesh);
        Assert.Contains("TransformNameMesh", mesh);
        Assert.DoesNotContain("WrapInput", mesh);
    }

    [Fact]
    public void Map_Target_And_Seating_Can_Get_A_Missing_Template_Back()
    {
        var map = Offered(Parse("Map(\"m\") { BackdropSmall(\"BackdropSmall\") { } }"));
        Assert.Contains("BackdropLarge", map);
        Assert.Contains("NorthSpawn", map);
        Assert.DoesNotContain("BackdropSmall", map);

        var target = Offered(Parse("Target(\"t\") { }"));
        Assert.Equal(24, target.Count(n => n.EndsWith("Screen") || n.EndsWith("ScreenBehind")));
        Assert.Contains("HintFlagOffScreenBehind", target);

        var seating = Offered(Parse("VehicleSeating(\"v\") { }"));
        Assert.Contains("AIText", seating);
        Assert.DoesNotContain("BackdropSmall", seating);
        Assert.DoesNotContain("AIText", Offered(Parse("Group(\"g\") { }")));
    }

    [Fact]
    public void Added_Blocks_And_Lines_Are_What_The_Reader_Reads()
    {
        // Every starting value reads back as its key, without a parse error.
        foreach (var def in PropertyCatalog.GetAddable(Parse("ProceduralBarBitmap(\"p\") { }")))
        {
            var block = def.Factory();
            Assert.Equal(def.Name, block.Name);
            Assert.Equal(def.IsBlock, block.HasBody);
        }
        Assert.True(ElementKinds.TryGet(Parse("ProceduralBarBitmap(\"p\") { }"), out _));
    }
}
