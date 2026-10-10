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

    /// <summary>A .cfg as the game's tools write one: each string UTF-16 after a 4-byte header, every byte's nibbles swapped.</summary>
    private static string Cfg(params (string Scope, string Name, string Text)[] strings)
    {
        static string Hex(string text)
        {
            var bytes = new List<byte> { 0x01, 0x00, 0x00, 0x00 };
            foreach (var c in text) { bytes.Add((byte)c); bytes.Add((byte)(c >> 8)); }
            return string.Concat(bytes.Select(b => ((byte)((b >> 4) | ((b & 0x0F) << 4))).ToString("X2")));
        }
        var body = string.Concat(strings.Select(s =>
            $"  VarScope(\"{s.Scope}\")\n  {{\n    VarBinary(\"{s.Name}\")\n    {{\n      Value(\"{Hex(s.Text)}\");\n    }}\n  }}\n"));
        return $"DataBase()\n{{\n{body}}}\n";
    }

    [Fact]
    public void Layers_Lay_Over_Each_Other_In_Order()
    {
        var dir = Directory.CreateTempSubdirectory("vh-loc-").FullName;
        try
        {
            var stock = Path.Combine(dir, "english.cfg");
            var pc = Path.Combine(dir, "pc.cfg");
            File.WriteAllText(stock, Cfg(("game", "a", "stock a"), ("game", "b", "stock b")));
            File.WriteAllText(pc, Cfg(("game", "a", "PC a")));
            var table = LocalizationTable.LoadLayered(new[] { stock, pc });
            Assert.Equal("PC a", table.TryGet("game.a"));
            Assert.Equal("stock b", table.TryGet("game.b"));
            Assert.Equal(pc, table.SourceOf("game.a"));
            Assert.Equal(stock, table.SourceOf("game.b"));
            Assert.Null(table.SourceOf("game.c"));
            Assert.Equal(new[] { stock, pc }, table.Files);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_Mods_Strings_Lie_Over_The_Stock_Ones_With_Each_Ones_PC_Strings_On_Top()
    {
        var dir = Directory.CreateTempSubdirectory("vh-loc-").FullName;
        try
        {
            string Put(string relative, string text)
            {
                var path = Path.Combine(dir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
                return path;
            }
            var stock = Put(@"data\Common\Localize\english.cfg", Cfg(("game", "a", "stock")));
            var stockPc = Put(@"data\Common\Localize\PC\English.cfg", Cfg(("game", "a", "stock PC")));
            var mod = Put(@"data_MOD\Common\Localize\english.cfg", Cfg(("game", "a", "mod")));
            var modPc = Put(@"data_MOD\Common\Localize\PC\English.cfg", Cfg(("game", "b", "mod PC")));
            var hud = Put(@"data_MOD\Common\hud\my.hud", "FileInfo(\"my\") { Viewports(1) }");

            var assets = SwbfHudEditor.Core.Assets.AssetResolver.ForHudFiles(new[] { hud }, Path.Combine(dir, @"data\Common"));
            Assert.Equal(new[] { stock, stockPc, mod, modPc }, assets.FindLocalizationLayers("english"));

            var table = LocalizationTable.LoadLayered(assets.FindLocalizationLayers("english"));
            Assert.Equal("mod", table.TryGet("game.a"));
            Assert.Equal("mod PC", table.TryGet("game.b"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_BF3_Mod_Reads_Its_Own_Strings_Over_The_Stock_Ones()
    {
        const string hud = @"C:\BF2_ModTools\data_BF3\Common\hud\legacy_gameext_hud.hud";
        if (!File.Exists(hud) || !File.Exists(EnglishCfg)) return;
        var layers = SwbfHudEditor.Core.Assets.AssetResolver.ForHudFiles(new[] { hud }, @"C:\BF2_ModTools\data\Common")
            .FindLocalizationLayers("english");
        foreach (var layer in layers) _out.WriteLine(layer);
        Assert.Equal(@"C:\BF2_ModTools\data\Common\Localize\english.cfg", layers[0], ignoreCase: true);
        Assert.EndsWith(@"data_BF3\Common\Localize\PC\English.cfg", layers[^1], StringComparison.OrdinalIgnoreCase);
        var table = LocalizationTable.LoadLayered(layers);
        Assert.True(table.Count > 1000);
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
