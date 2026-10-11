using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SwbfHudEditor.Core.Events;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Tests;

/// <summary>
/// Parity with BF2GameExt: every event, property, transform, transform parameter and INI setting that GameExt's HUD
/// reference (<c>docs/user/HUD_PROPERTIES.md</c>) lists is one the editor knows. The reference is read from the
/// BF2GameExt checkout beside this repository, or from <c>VISUALHUD_GAMEEXT</c>; without one the test has nothing to
/// check. See docs/GameExtParity.md.
/// </summary>
public class GameExtParityTests
{
    /// <summary>The INI settings the reference lists, and what the editor does with each.</summary>
    private static readonly Dictionary<string, string> Settings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["[Features] HudSubPixel"] = "Modelled: under GameExt the canvas draws without the stock game's snap to whole units.",
        ["[Features] TargetBarLatchSeconds"] = "Run time only: how long a target's position lasts; the Events tab sends positions by hand.",
        ["[Fixes] ReticleCorrection"] = "Run time only: it moves the reticle's position event onto the aim point.",
        ["[Fixes] WeaponIconFix"] = "Not modelled yet: an unmapped TransformNameMesh still sends the model by name, as in the stock game.",
    };

    private static string SourceFile([CallerFilePath] string path = "") => path;

    /// <summary>GameExt's HUD reference: from VISUALHUD_GAMEEXT, else the BF2GameExt checkout beside this repository.</summary>
    private static string? Reference()
    {
        var root = Environment.GetEnvironmentVariable("VISUALHUD_GAMEEXT") is { Length: > 0 } set
            ? set
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceFile())!, "..", "..", "..", "BF2GameExt"));
        var path = Path.Combine(root, "docs", "user", "HUD_PROPERTIES.md");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Each table row whose first cell is a name in backticks, with the section and subsection it is under.</summary>
    private static List<(string Section, string Sub, string Name, string Second)> Rows(string path)
    {
        var rows = new List<(string, string, string, string)>();
        string section = string.Empty, sub = string.Empty;
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                (section, sub) = (line[3..].Trim(), string.Empty);
                continue;
            }
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                sub = line[4..].Trim();
                continue;
            }
            var m = Regex.Match(line, @"^\|\s*`([^`]+)`\s*\|\s*([^|]*)\|");
            if (m.Success) rows.Add((section, sub, m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));
        }
        return rows;
    }

    /// <summary>An empty block of that type, as a file would have it.</summary>
    private static HudBlock Block(string type) => HudParser.Parse($"{type}(\"x\")\n{{\n}}\n").TopLevel.First(b => b.HasBody);

    /// <summary>Why the editor does not know a row, or null when it does.</summary>
    private static string? Unknown((string Section, string Sub, string Name, string Second) row)
    {
        var (section, sub, name, second) = row;
        if (section == "Events")
        {
            // commandPostN, weaponN: the first of them; weapon2.state.*: any of them.
            var concrete = Regex.Replace(name, @"(?<=[A-Za-z])N(?=\.)", "1");
            if (concrete.IndexOf('*') is var star and >= 0)
                return HudEventCatalog.All.Any(e => e.Source == HudEventSource.GameExt &&
                                                     e.Name.StartsWith(concrete[..star], StringComparison.OrdinalIgnoreCase))
                    ? null : "no GameExt event in the catalog starts so";
            return HudEventCatalog.Find(concrete) switch
            {
                null => "not in the event catalog",
                { Source: not HudEventSource.GameExt } => "in the event catalog, but not as GameExt's",
                _ => null,
            };
        }
        if (section == "Element Properties")
        {
            // The second cell names the class that reads it, or says any element does.
            var cls = Regex.Match(second, "`([A-Za-z0-9]+)`") is { Success: true } c ? c.Groups[1].Value : "Bitmap";
            return PropertyCatalog.Find(Block(cls), null, name) is null ? $"no {cls} line in the property catalog" : null;
        }
        if (section == "Transforms")
        {
            if (sub.Length == 0) return HudBlockTemplates.Find(name) is null ? "no template in the Insert menus" : null;
            return PropertyCatalog.Find(Block(sub), null, name) is null ? $"no {sub} line in the property catalog" : null;
        }
        if (section.StartsWith("INI", StringComparison.Ordinal))
            return Settings.ContainsKey(name) ? null : "not in this test's list of settings, saying what the editor does with it";
        return $"a section this test does not read yet: \"{section}\"";
    }

    [Fact]
    public void Everything_GameExts_HUD_Reference_Lists_Is_Known()
    {
        if (Reference() is not { } path) return;   // no BF2GameExt checkout beside this one
        var rows = Rows(path);
        Assert.True(rows.Count > 50, $"Only {rows.Count} names read from {path}: has its layout changed?");
        var unknown = rows.Select(r => (Row: r, Why: Unknown(r))).Where(x => x.Why is not null)
            .Select(x => $"{x.Row.Section} > {(x.Row.Sub.Length > 0 ? x.Row.Sub + " > " : "")}{x.Row.Name}: {x.Why}")
            .ToList();
        Assert.True(unknown.Count == 0,
            "VisualHUD does not know these from BF2GameExt's HUD reference (docs/GameExtParity.md):\n" + string.Join("\n", unknown));
    }
}
