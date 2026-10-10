using System.IO;
using System.Numerics;
using System.Windows.Input;
using SwbfHudEditor.Core.Events;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.App.Tests;

/// <summary>Nudging, saved scenarios, backups on save and loading a file again.</summary>
public class QolTests
{
    private const string Hud = """
        FileInfo("test")
        {
            Viewports(1)
        }

        Group("group")
        {
            Viewport(1)
            Position(0.5, 0.5, 0, "Viewport")

            Bitmap("icon")
            {
                Position(10, 20, 0)
                BitmapRect(32, 32, "Left", "Top", "Pixels")
            }
        }

        """;

    private static HudBlock Named(MainWindow w, string name) =>
        All(w.Files.SelectMany(f => f.Document.TopLevel)).First(b => b.HasBody && b.ItemName == name);

    private static IEnumerable<HudBlock> All(IEnumerable<HudBlock> blocks)
    {
        foreach (var b in blocks)
        {
            yield return b;
            foreach (var inner in All(b.Children)) yield return inner;
        }
    }

    private static Vector2 Origin(MainWindow w, string name) =>
        Vector2.Transform(Vector2.Zero, w.Canvas.LaidOut(Named(w, name))!.ToScreen);

    [Fact]
    public void Arrow_Nudges_Move_By_Screen_Pixels_And_A_Run_Undoes_As_One()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            w.SelectByName("icon");
            var before = Origin(w, "icon");
            w.Nudge(1, 0);
            w.Nudge(1, 0);
            w.Nudge(0, 10);
            var after = Origin(w, "icon");
            Assert.Equal(2f, after.X - before.X, 2);
            Assert.Equal(10f, after.Y - before.Y, 2);
            // One undo step takes the whole run back.
            ApplicationCommands.Undo.Execute(null, w);
            Assert.True(Vector2.Distance(before, Origin(w, "icon")) < 0.01f);

            // A group and its child picked together move once, with the group.
            w.SelectByName("group");
            w.TogglePick(Named(w, "icon"));
            var group = Origin(w, "group");
            var icon = Origin(w, "icon");
            w.Nudge(5, 0);
            Assert.Equal(5f, Origin(w, "group").X - group.X, 2);
            Assert.Equal(5f, Origin(w, "icon").X - icon.X, 2);
        });
    }

    [Fact]
    public void A_Scenario_Saves_The_Sends_With_Their_Times_And_Sends_Them_Again()
    {
        UiThread.WithWindow(Hud, (w, _) =>
        {
            var state = w.EventState;
            state.Send("player1.healthFraction", HudEventValue.FromFloat(0.123456f));
            state.SetTime(2.5);
            state.Send("player1.weapon1.ammo", HudEventValue.FromInt(7));
            w.SaveScenario("test run");

            state.Clear();
            Assert.Empty(state.History);
            w.ApplyScenario("test run");
            Assert.Equal(2, state.History.Count);
            Assert.Equal(0.123456f, state.Sends["player1.healthFraction"].Value.Float);
            Assert.Equal(2.5, state.Sends["player1.weapon1.ammo"].Time, 6);
            Assert.Equal(0, state.Now);

            w.DeleteScenario("test run");
            state.Clear();
            w.ApplyScenario("test run");
            Assert.Empty(state.History);
        });
    }

    [Fact]
    public void Saving_Copies_The_File_As_It_Was_Into_Backups()
    {
        UiThread.WithWindow(Hud, (w, path) =>
        {
            var before = File.ReadAllText(path);
            w.Nudge(0, 0);   // nothing picked: no change
            w.SelectByName("icon");
            w.Nudge(1, 0);
            ApplicationCommands.Save.Execute(null, w);
            var backups = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(path)!, HudBackups.FolderName), "test.hud.*.bak");
            Assert.Equal(before, File.ReadAllText(Assert.Single(backups)));
            Assert.NotEqual(before, File.ReadAllText(path));
        });
    }

    [Fact]
    public void A_File_Changed_On_Disk_Loads_Again_In_Its_Place()
    {
        UiThread.WithWindow(Hud, (w, path) =>
        {
            File.WriteAllText(path, Hud.Replace("Position(10, 20, 0)", "Position(30, 40, 0)"));
            w.ReloadFromDisk(w.Files.Single(f => f.FilePath == path));
            var line = Named(w, "icon").Children.First(c => c.Name == "Position");
            Assert.Equal("30", line.Arguments[0].Raw);
        });
    }
}
