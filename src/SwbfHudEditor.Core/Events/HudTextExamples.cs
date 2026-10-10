using System.Text;
using System.Text.RegularExpressions;
using SwbfHudEditor.Core.Layout;

namespace SwbfHudEditor.Core.Events;

/// <summary>What the preview puts in a text element before any event has been sent.</summary>
public enum TextPreview
{
    /// <summary>Typical values: what the events would send in a game, and the strings of the keys.</summary>
    Examples,
    /// <summary>Long values, to see how a box holds a long name, a large number or a paragraph.</summary>
    Long,
    /// <summary>The keys and event names the file names.</summary>
    Keys,
}

/// <summary>
/// Example text for text elements, so a HUD can be judged with the kind of text the game puts in it:
/// a <c>Text</c> key's string (the key itself when the localization lacks it, as the game shows it),
/// and for <c>EventText</c> and <c>EventNumber</c> a typical value of that event, a number printed with
/// the element's own format.
/// </summary>
public static partial class HudTextExamples
{
    public static string? For(ElementSpec spec, TextPreview mode, Func<string, string?>? localize)
    {
        var keys = mode == TextPreview.Keys;
        var large = mode == TextPreview.Long;
        if (spec.TextKey is { } key)
            return keys ? key : WithButtons(localize?.Invoke(key) ?? key);
        if (spec.EventText is { } text)
            return keys ? Placeholder(text) : TextOf(text, large);
        if (spec.EventNumber is { } number)
            return keys ? Placeholder(number) : HudSimulation.FormatNumber(spec, NumberOf(number, large)) ?? string.Empty;
        return null;
    }

    /// <summary>The event's last part in braces, as the Keys preview shows an event.</summary>
    public static string Placeholder(string eventName)
    {
        var dot = eventName.LastIndexOf('.');
        return "{" + (dot < 0 ? eventName : eventName[(dot + 1)..]) + "}";
    }

    // Typical and long values, by event; the player's number does not matter.
    private static readonly (Regex Name, string Typical, string Long)[] Texts =
    {
        (Rx(@"weapon\d\.name$"), "DC-15A Blaster Rifle", "Wookiee Bowcaster Heavy Repeater Rifle"),
        (Rx(@"(target\.name|lockOnName)$"), "CT-7567 Rex", "Clone Commander Bacara of the 21st Corps"),
        (Rx(@"(target\.className|lockOnClassName)$"), "Clone Trooper", "Advanced Heavy Assault Clone Trooper"),
        (Rx(@"(target\.shieldName|lockOnShieldName)$"), "Deflector Shield", "Experimental Personal Deflector Shield"),
        (Rx(@"spawnDisplay\.vehicle$"), "AT-TE", "All Terrain Tactical Enforcer Walker"),
        (Rx(@"spawnDisplay\.spawninfo$"), "Press Fire to spawn", "Select a command post on the map, then press Fire to spawn there"),
        (Rx(@"spawnDisplay\.message$"), "Waiting for reinforcements", "Waiting for reinforcements: you will spawn with the next wave"),
        (Rx(@"heroSelect\.message$"), "Select your hero", "Your team has chosen you as its hero: select a hero to play"),
        (Rx(@"\.message$"), "Command post captured", "Your team has captured the command post at the Jedi Temple"),
        (Rx(@"(hintPopup|objectivePopup|selectionPopup)$"),
            "Capture command posts to drain the enemy's reinforcements.",
            "Capture command posts to drain the enemy's reinforcements. When their count reaches zero, your team wins. " +
            "Hold more posts than the enemy and their reinforcements fall faster."),
        (Rx(@"hintPopup\.pageNumber$"), "1/3", "12/15"),
        (Rx(@"tooltips$"), "Press Use to enter the vehicle", "Press Use to enter the vehicle, then Use again to change seats"),
        (Rx(@"Delta$"), "+1", "+250"),
        (Rx(@"pointsText$"), "150", "99999"),
    };

    private static readonly (Regex Name, double Typical, double Long)[] Numbers =
    {
        (Rx(@"totalAmmoBullets$"), 120, 999),
        (Rx(@"totalClipBullets$"), 30, 250),
        (Rx(@"reinforcements$"), 150, 9999),
        (Rx(@"team\d\.points$"), 42, 9999),
        (Rx(@"statistic\.kills$"), 12, 999),
        (Rx(@"statistic\.points$"), 340, 99999),
        (Rx(@"(objective|victory|defeat)timer$"), 125, 5999),
        (Rx(@"heroSelect\.timer$"), 15, 120),
        (Rx(@"(lockOnDistance|missileLockDistance|distance)$"), 125.3, 2048.7),
        (Rx(@"(health|lockOnHealth|target\.health)$"), 300, 99999),
        (Rx(@"Fraction$"), 0.75, 1),
        (Rx(@"flag\.\w+\.\w+\.number$"), 2, 99),
        (Rx(@"time$"), 125, 5999),
    };

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string TextOf(string eventName, bool large)
    {
        foreach (var (name, typical, longer) in Texts)
            if (name.IsMatch(eventName)) return large ? longer : typical;
        return large ? "A long sample text, to see how the box holds it" : "Sample text";
    }

    /// <summary>A typical value of a number event, of the type the game sends it as.</summary>
    private static HudEventValue NumberOf(string eventName, bool large)
    {
        var value = large ? 99999.0 : 42.0;
        foreach (var (name, typical, longer) in Numbers)
            if (name.IsMatch(eventName))
            {
                value = large ? longer : typical;
                break;
            }
        return (HudEventCatalog.Find(eventName)?.Type ?? HudEventType.Int) switch
        {
            HudEventType.Float => HudEventValue.FromFloat((float)value),
            HudEventType.Uint => HudEventValue.FromUint((uint)value),
            _ => HudEventValue.FromInt((int)value),
        };
    }

    /// <summary>
    /// The game swaps a <c>{name}</c> in a string for the button bound to it
    /// (<c>FindReplaceMagicStrings_</c>); the preview writes the action, as <c>[Accept]</c> for
    /// <c>{GuiAccept}</c>.
    /// </summary>
    public static string WithButtons(string text) => ButtonPattern().Replace(text, m => "[" + Action(m.Groups[1].Value) + "]");

    private static string Action(string name)
    {
        var words = new StringBuilder();
        var bare = Regex.Replace(name, @"^(btn_|Gui)|(_?button)$", string.Empty, RegexOptions.IgnoreCase);
        if (bare.Length == 0) bare = name;
        foreach (var part in bare.Split('_', StringSplitOptions.RemoveEmptyEntries))
            foreach (Match word in Regex.Matches(part, @"[A-Z]?[a-z0-9]+|[A-Z]+(?![a-z])"))
            {
                if (words.Length > 0) words.Append(' ');
                words.Append(char.ToUpperInvariant(word.Value[0])).Append(word.Value[1..]);
            }
        return words.Length > 0 ? words.ToString() : name;
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex ButtonPattern();
}
