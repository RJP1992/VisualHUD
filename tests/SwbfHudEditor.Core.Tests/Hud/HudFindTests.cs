using SwbfHudEditor.Core.Edits;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Tests.Hud;

/// <summary>Find and replace in arguments: where it looks, what it counts, and what it will not write.</summary>
public class HudFindTests
{
    private const string First = """
        Group("player1weapon")
        {
            Position(10, 0.5, 0, "Viewport")
            EventEnable("player1.weapon1.ammo")

            Text("player1ammo")
            {
                EventText("player1.weapon1.ammo")
                Visible(true)
            }
        }

        """;

    private const string Second = """
        Bitmap("Player1Icon")
        {
            Bitmap("hud_player1")
            Alpha(10)
        }

        """;

    private static List<HudDocument> Documents() => new() { HudParser.Parse(First), HudParser.Parse(Second) };

    [Fact]
    public void Finds_The_Text_In_Strings_With_Their_Element_And_How_Often()
    {
        var documents = Documents();
        var matches = HudFind.Find(documents, new FindQuery("player1"));
        // Names, events and a texture, in both files, in order; the numbers are not looked at.
        Assert.Equal(new[] { "player1weapon", "player1.weapon1.ammo", "player1ammo", "player1.weapon1.ammo", "Player1Icon", "hud_player1" },
            matches.Select(m => m.Value.Raw));
        Assert.All(matches, m => Assert.Equal(1, m.Count));
        var eventText = matches[3];
        Assert.Equal("EventText", eventText.Line.Name);
        Assert.Equal("player1ammo", eventText.Element!.ItemName);
        Assert.Same(documents[1], matches[4].Document);
        Assert.Same(matches[4].Line, matches[4].Element);   // a name: the element's own header

        Assert.Equal(5, HudFind.Find(documents, new FindQuery("player1", MatchCase: true)).Count);
        Assert.Equal(2, HudFind.Find(documents, new FindQuery("player1.weapon1.ammo", WholeValue: true)).Count);
        Assert.Empty(HudFind.Find(documents, new FindQuery("player1.weapon1", WholeValue: true)));
        Assert.Equal(1, HudFind.Occurrences("a.a.a", new FindQuery("a.a")));   // not overlapping, as the replace goes
        Assert.Equal("b.a", HudFind.Replace("a.a.a", new FindQuery("a.a"), "b"));
        Assert.Empty(HudFind.Find(documents, new FindQuery("")));
    }

    [Fact]
    public void All_Values_Looks_In_Numbers_And_Bare_Words_Too()
    {
        var matches = HudFind.Find(Documents(), new FindQuery("10", StringsOnly: false));
        Assert.Equal(new[] { "Position", "Alpha" }, matches.Select(m => m.Line.Name));
        Assert.Single(HudFind.Find(Documents(), new FindQuery("TRUE", WholeValue: true, StringsOnly: false)));
    }

    [Fact]
    public void Replacing_Changes_Every_Match_In_One_Undo_Step()
    {
        var documents = Documents();
        var query = new FindQuery("player1.", MatchCase: true);
        var (changes, refused) = HudFind.Changes(HudFind.Find(documents, query), query, "player2.");
        Assert.Empty(refused);
        Assert.Equal(2, changes.Count);
        var edit = new ArgumentsEdit(changes, "replace");
        edit.Apply();
        Assert.Contains("EventText(\"player2.weapon1.ammo\")", HudWriter.Write(documents[0]));
        Assert.Empty(HudFind.Find(documents, query));
        edit.Revert();
        Assert.Equal(First, HudWriter.Write(documents[0]));
    }

    [Fact]
    public void A_Value_That_Would_Not_Read_Back_Is_Left_As_It_Is()
    {
        var documents = Documents();
        // A number made a float where it takes a point; not a number at all, refused.
        var query = new FindQuery("10", WholeValue: true, StringsOnly: false);
        var (changes, refused) = HudFind.Changes(HudFind.Find(documents, query), query, "12.5");
        Assert.Equal(2, changes.Count);
        Assert.All(changes, c => Assert.Equal(HudArgKind.Float, c.New.Kind));
        (changes, refused) = HudFind.Changes(HudFind.Find(documents, query), query, "ten");
        Assert.Empty(changes);
        Assert.Equal(2, refused.Count);

        // A quote would end the string; a bare word stays a word.
        query = new FindQuery("hud_player1", WholeValue: true);
        Assert.Single(HudFind.Changes(HudFind.Find(documents, query), query, "say \"hi\"").Refused);
        Assert.Null(HudFind.Retype(HudArgument.FromIdentifier("true"), "not true"));
        Assert.Equal(HudArgument.FromIdentifier("false"), HudFind.Retype(HudArgument.FromIdentifier("true"), "false"));
        Assert.Equal(HudArgKind.Integer, HudFind.Retype(HudArgument.FromFloat(1), "-3")!.Kind);
        Assert.Null(HudFind.Retype(HudArgument.FromInt(1), "1e"));

        // Replaced with the same text in another case: a change; with the same text: none.
        query = new FindQuery("PLAYER1ICON", WholeValue: true);
        Assert.Single(HudFind.Changes(HudFind.Find(documents, query), query, "player1icon").Changes);
        Assert.Empty(HudFind.Changes(HudFind.Find(documents, query), query, "Player1Icon").Changes);
    }
}
