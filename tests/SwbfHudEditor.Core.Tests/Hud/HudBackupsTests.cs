using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Tests.Hud;

/// <summary>Copies made before a save: beside the file, the newest kept, nothing else touched.</summary>
public class HudBackupsTests
{
    [Fact]
    public void Keeps_The_Newest_Copies_Of_A_File_And_Leaves_Other_Files_Alone()
    {
        var folder = Directory.CreateTempSubdirectory("visualhud-backups-").FullName;
        try
        {
            var path = Path.Combine(folder, "my.hud");
            File.WriteAllText(path, "FileInfo(\"x\") { }");
            var backups = Path.Combine(folder, HudBackups.FolderName);
            Directory.CreateDirectory(backups);
            var own = Path.Combine(backups, "my.hud.before_authored169.bak");   // the user's own, not a copy this makes
            var other = Path.Combine(backups, "other.hud.20260101-000000.bak");
            File.WriteAllText(own, "mine");
            File.WriteAllText(other, "other");

            var start = new DateTime(2026, 10, 10, 12, 0, 0);
            string? last = null;
            for (var i = 0; i < HudBackups.Keep + 5; i++) last = HudBackups.Backup(path, start.AddSeconds(i));

            var copies = Directory.GetFiles(backups, "my.hud.2026*.bak");
            Assert.Equal(HudBackups.Keep, copies.Length);
            Assert.Contains(last!, copies);
            Assert.DoesNotContain(copies, c => c.EndsWith("120000.bak", StringComparison.Ordinal));   // the oldest went
            Assert.True(File.Exists(own));
            Assert.True(File.Exists(other));
            Assert.Null(HudBackups.Backup(Path.Combine(folder, "missing.hud"), start));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
