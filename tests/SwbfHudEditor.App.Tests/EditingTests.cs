using System.IO;
using System.Windows.Input;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Tests;

/// <summary>The editor's commands on a real window: insert, select several, show and hide, copy, move, undo.</summary>
public class EditingTests
{
    private const string Hud = """
        FileInfo("test")
        {
        	Viewports(1)
        }

        Group("holder")
        {
        	Viewport(1)
        	Position(0.500000, 0.500000, 0.000000, "Viewport")
        	EventEnable("initialize")

        	Text("label")
        	{
        		EventEnable("initialize")
        	}

        	Bitmap("icon")
        	{
        		EventEnable("initialize")
        	}
        }

        Group("other")
        {
        	EventEnable("initialize")
        }

        Model3D("weapon")
        {
        	Mesh("hud_infinite_ammo_icon")
        	EventEnable("initialize")
        }

        Model3D("copy")
        {
        	InheritMeshInfo("weapon")
        	EventEnable("initialize")
        }

        """;

    private static HudBlock Block(MainWindow w, string name) =>
        All(w.Files.SelectMany(f => f.Document.TopLevel)).First(b => b.HasBody && b.ItemName == name);

    private static IEnumerable<HudBlock> All(IEnumerable<HudBlock> blocks)
    {
        foreach (var b in blocks)
        {
            yield return b;
            foreach (var inner in All(b.Children)) yield return inner;
        }
    }

    private static string Saved(MainWindow w, string path)
    {
        var file = w.Files.Single(f => f.FilePath == path);
        return HudWriter.Write(file.Document);
    }

    [Fact]
    public void Insert_Puts_A_Working_Element_Into_The_Selected_Group()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            Assert.True(w.SelectByName("holder"));
            var bar = w.InsertTemplate(HudBlockTemplates.Find("BarBitmap")!, inside: true)!;
            UiThread.Settle();
            var holder = Block(w, "holder");
            Assert.Same(bar, holder.Children.Last());
            Assert.Equal("new_barbitmap", bar.ItemName);
            // Inside a group it sits at the group's origin and takes the group's screen.
            Assert.DoesNotContain(bar.Children, c => c.Name == "Viewport");
            Assert.Equal("0.000000", bar.Children.First(c => c.Name == "Position").Arguments[0].Raw);

            // A Text holds nothing: the insert goes nowhere.
            w.SelectByName("label");
            Assert.Null(w.InsertTemplate(HudBlockTemplates.Find("Bitmap")!, inside: true));

            ApplicationCommands.Undo.Execute(null, w);
            Assert.DoesNotContain(bar, holder.Children);
        });
    }

    [Fact]
    public void Several_Rows_Are_Hidden_Shown_Alone_And_Deleted_Together()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            w.SelectByName("label");
            w.TogglePick(Block(w, "icon"));
            Assert.Equal(new[] { "label", "icon" }, w.SelectedItems.Cast<HudBlock>().Select(b => b.ItemName));

            Assert.True(w.ToggleHideSelected());
            Assert.False(Block(w, "label").IsVisible);
            Assert.False(Block(w, "icon").IsVisible);
            Assert.True(Block(w, "holder").IsVisible);

            w.ToggleHideSelected();
            w.ShowOnly(w.SelectedItems);
            Assert.True(Block(w, "label").IsVisible);
            Assert.True(Block(w, "holder").IsVisible);           // holds them
            Assert.False(Block(w, "other").IsVisible);
            Assert.False(Block(w, "weapon").IsVisible);
            w.ShowEverything();
            Assert.True(Block(w, "other").IsVisible);

            var holder = Block(w, "holder");
            var label = Block(w, "label");
            var at = holder.Children.IndexOf(label);
            w.DeleteSelected();
            Assert.DoesNotContain(holder.Children, c => c.ItemName is "label" or "icon");
            ApplicationCommands.Undo.Execute(null, w);
            Assert.Same(label, holder.Children[at]);
            Assert.Contains(holder.Children, c => c.ItemName == "icon");
        });
    }

    [Fact]
    public void A_Ctrl_Click_On_The_Only_Selected_Row_Keeps_It_And_A_Range_Picks_The_Rows_Between()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            w.SelectByName("other");
            w.TogglePick(Block(w, "other"));
            Assert.Equal(new object[] { Block(w, "other") }, w.SelectedItems);

            // From "other" to "copy" as the tree shows them: other, weapon, copy.
            w.PickRange(Block(w, "copy"), add: false);
            Assert.Equal(new[] { "other", "weapon", "copy" }, w.SelectedItems.Cast<HudBlock>().Select(b => b.ItemName));
        });
    }

    [Fact]
    public void Duplicate_And_Paste_Give_The_Copies_Names_Of_Their_Own()
    {
        UiThread.WithWindow(Hud, (w, path) =>
        {
            w.SelectByName("label");
            Assert.True(w.DuplicateSelection());
            var holder = Block(w, "holder");
            var label = Block(w, "label");
            Assert.Equal("label_1", holder.Children[holder.Children.IndexOf(label) + 1].ItemName);

            // Blocks pasted into the selected group, renamed where the names are taken.
            w.SelectByName("holder");
            Assert.True(w.Paste("Bitmap(\"icon\")\n{\n    Alpha(0.5)\n}\n"));
            Assert.Contains(holder.Children, c => c.ItemName == "icon_1");

            // Lines pasted into the selected element: a line it has once is replaced, a new one added.
            w.SelectByName("icon");
            Assert.True(w.Paste("Alpha(0.25)\nEventEnable(\"player1.spawn\")"));
            var icon = Block(w, "icon");
            Assert.Equal("player1.spawn", icon.Children.Single(c => c.Name == "EventEnable").Arguments[0].Raw);
            Assert.Equal("0.25", icon.Children.Single(c => c.Name == "Alpha").Arguments[0].Raw);

            // Undone, the line is as it was.
            ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal("initialize", icon.Children.Single(c => c.Name == "EventEnable").Arguments[0].Raw);
            Assert.DoesNotContain(icon.Children, c => c.Name == "Alpha");

            // What is not .hud text pastes nothing.
            Assert.False(w.Paste("hello there"));
        });
    }

    [Fact]
    public void Move_Up_Down_And_Into_Another_Group_Undo_In_Place()
    {
        UiThread.WithWindow(Hud, (w, path) =>
        {
            var holder = Block(w, "holder");
            w.SelectByName("icon");
            Assert.True(w.MoveSelection(-1));
            Assert.Equal(new[] { "icon", "label" }, holder.Children.Where(c => c.HasBody).Select(c => c.ItemName));
            ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal(new[] { "label", "icon" }, holder.Children.Where(c => c.HasBody).Select(c => c.ItemName));

            // A drop into another group: re-parented, then back on undo.
            var other = Block(w, "other");
            var icon = Block(w, "icon");
            Assert.True(w.MoveBlocks(new[] { icon }, other.Children, other.Children.Count));
            Assert.Same(icon, other.Children.Last());
            Assert.DoesNotContain(icon, holder.Children);
            Assert.Contains("\tGroup(\"other\")", "\t" + Saved(w, path).Split('\n').First(l => l.Contains("\"other\"")).Trim());
            ApplicationCommands.Undo.Execute(null, w);
            Assert.Contains(icon, holder.Children);
            Assert.DoesNotContain(icon, other.Children);
        });
    }

    [Fact]
    public void Rename_Takes_The_Lines_That_Name_It_Along()
    {
        UiThread.WithWindow(Hud, (w, path) =>
        {
            var weapon = Block(w, "weapon");
            Assert.True(w.ApplyRename(weapon, "rifle"));
            Assert.Equal("rifle", weapon.ItemName);
            Assert.Contains("InheritMeshInfo(\"rifle\")", Saved(w, path));
            Assert.Contains("InheritMeshInfo", w.Status);
            ApplicationCommands.Undo.Execute(null, w);
            Assert.Equal("weapon", weapon.ItemName);
        });
    }

    [Fact]
    public void Saving_Writes_The_Edits_In_The_File_s_Own_Indentation()
    {
        UiThread.WithWindow(Hud, (w, path) =>
        {
            w.SelectByName("other");
            w.InsertTemplate(HudBlockTemplates.Find("Text")!, inside: true);
            UiThread.Settle();
            ApplicationCommands.Save.Execute(null, w);
            var text = File.ReadAllText(path);
            Assert.Contains("\tText(\"new_text\")\n\t{\n\t\tText(\"game.objectives.updated\")", text.Replace("\r\n", "\n"));
        });
    }
}
