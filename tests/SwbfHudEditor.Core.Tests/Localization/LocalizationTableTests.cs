using SwbfHudEditor.Core.Localization;
using Xunit.Abstractions;

namespace SwbfHudEditor.Core.Tests.Localization;

public class LocalizationTableTests
{
    private const string EnglishCfg = @"C:\BF2_ModTools\data\Common\Localize\english.cfg";
    private readonly ITestOutputHelper _out;
    public LocalizationTableTests(ITestOutputHelper output) { _out = output; }

    [Fact]
    public void Decodes_Known_Keys()
    {
        if (!File.Exists(EnglishCfg)) return;
        var table = LocalizationTable.LoadFromCfg(EnglishCfg);
        Assert.True(table.Count > 100, $"Expected many keys, got {table.Count}");

        // Spot checks — decoded values preserve Pandemic's {OptionL}/{OptionR} controller
        // placeholders and may be format templates. We just assert the core word is present.
        var never = table.TryGet("common.never2");
        Assert.NotNull(never);
        Assert.Contains("Never", never);

        var quit = table.TryGet("common.quit2windows");
        Assert.NotNull(quit);
        Assert.True(quit.Length > 0);

        var reset = table.TryGet("common.reset");
        Assert.NotNull(reset);
        Assert.True(reset.Length > 0);

        _out.WriteLine($"common.never2       = \"{never}\"");
        _out.WriteLine($"common.quit2windows = \"{quit}\"");
        _out.WriteLine($"common.reset        = \"{reset}\"");
    }

    [Fact]
    public void Dump_Keys_Matching_Weapon()
    {
        if (!File.Exists(EnglishCfg)) return;
        var table = LocalizationTable.LoadFromCfg(EnglishCfg);
        // Reflect out all keys (just for this diagnostic — the class is intentionally closed).
        var field = typeof(LocalizationTable).GetField("_strings",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var dict = (Dictionary<string, string>)field!.GetValue(table)!;
        foreach (var kv in dict.Where(k => k.Key.Contains("eapon", StringComparison.OrdinalIgnoreCase)).Take(20))
            _out.WriteLine($"{kv.Key,-60} = \"{kv.Value}\"");
    }
}
