using System.Text;
using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Localization;

/// <summary>
/// Flat key → localized-string map parsed from SWBF2's <c>english.cfg</c> (and siblings).
///
/// Format: the .cfg file is a Pandemic ODF-style text wrapper around VarScope/VarBinary entries.
/// Each VarBinary holds a hex-encoded blob: 4-byte header + UTF-16 LE string data, with EACH BYTE
/// nibble-swapped (high/low 4-bit halves exchanged). Keys are the full dotted VarScope path plus
/// the VarBinary name — e.g. <c>"common.quit2windows"</c> → "Exit to Windows".
/// </summary>
public sealed class LocalizationTable
{
    private readonly Dictionary<string, string> _strings;

    private LocalizationTable(Dictionary<string, string> strings) { _strings = strings; }

    public int Count => _strings.Count;

    public string? TryGet(string key) => _strings.TryGetValue(key, out var s) ? s : null;

    private Dictionary<uint, string>? _byHash;

    /// <summary>
    /// The string whose key hashes to <paramref name="hash"/> (<c>RedLocalizeDB::Find</c> works by
    /// <c>PblHash</c>), as an <c>EventText</c> carrying a Uint looks it up.
    /// </summary>
    public string? TryGetByHash(uint hash)
    {
        if (_byHash is null)
        {
            _byHash = new Dictionary<uint, string>();
            foreach (var (key, value) in _strings) _byHash.TryAdd(Events.PblHash.Of(key), value);
        }
        return _byHash.TryGetValue(hash, out var s) ? s : null;
    }

    public static LocalizationTable LoadFromCfg(string path)
    {
        var src = File.ReadAllText(path);
        var doc = HudParser.Parse(src, path);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var top in doc.TopLevel)
            Walk(top, string.Empty, map);
        return new LocalizationTable(map);
    }

    public static LocalizationTable Empty() => new(new Dictionary<string, string>());

    private static void Walk(HudBlock block, string prefix, Dictionary<string, string> map)
    {
        switch (block.Name)
        {
            case "DataBase":
                foreach (var c in block.Children) Walk(c, prefix, map);
                break;

            case "VarScope":
                if (block.Arguments.Count >= 1 && block.Arguments[0].Kind == HudArgKind.String)
                {
                    var name = block.Arguments[0].AsString();
                    var newPrefix = prefix.Length == 0 ? name : $"{prefix}.{name}";
                    foreach (var c in block.Children) Walk(c, newPrefix, map);
                }
                break;

            case "VarBinary":
                if (block.Arguments.Count >= 1 && block.Arguments[0].Kind == HudArgKind.String)
                {
                    var name = block.Arguments[0].AsString();
                    var key = prefix.Length == 0 ? name : $"{prefix}.{name}";
                    var decoded = Decode(block);
                    if (decoded is not null) map[key] = decoded;
                }
                break;
        }
    }

    private static string? Decode(HudBlock block)
    {
        var hex = new StringBuilder();
        foreach (var c in block.Children)
        {
            if (c.Name == "Value" && !c.HasBody && c.Arguments.Count >= 1 && c.Arguments[0].Kind == HudArgKind.String)
                hex.Append(c.Arguments[0].AsString());
        }
        if (hex.Length < 8) return string.Empty;

        var bytes = HexToBytes(hex.ToString());
        if (bytes.Length < 4) return string.Empty;

        var sb = new StringBuilder();
        // Skip 4-byte header (text flags/format — not needed for display).
        for (var i = 4; i + 1 < bytes.Length; i += 2)
        {
            var lo = SwapNibbles(bytes[i]);
            var hi = SwapNibbles(bytes[i + 1]);
            var ch = (char)(lo | (hi << 8));
            if (ch == 0) break;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private static byte SwapNibbles(byte b) => (byte)((b >> 4) | ((b & 0x0F) << 4));

    private static byte[] HexToBytes(string hex)
    {
        var clean = new StringBuilder(hex.Length);
        foreach (var c in hex)
            if (IsHexDigit(c)) clean.Append(c);
        if (clean.Length % 2 != 0) clean.Length--;

        var bytes = new byte[clean.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)((HexValue(clean[i * 2]) << 4) | HexValue(clean[i * 2 + 1]));
        return bytes;
    }

    private static bool IsHexDigit(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => 0,
    };
}
