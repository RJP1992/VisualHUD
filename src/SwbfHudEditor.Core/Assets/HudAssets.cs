using SwbfHudEditor.Core.Hud;
using SwbfHudEditor.Core.Layout;
using SwbfHudEditor.Core.Msh;

namespace SwbfHudEditor.Core.Assets;

public enum HudAssetKind
{
    Texture,
    Mesh,
    Font,
    Localization,
}

/// <summary>A file a HUD names, where it was found (null when it is missing) and what names it.</summary>
public sealed record HudAsset(HudAssetKind Kind, string Name, string? Path, IReadOnlyList<string> UsedBy)
{
    public bool Missing => Path is null;
}

/// <summary>The files a HUD's <c>.hud</c> files name, and whether each can be found.</summary>
public static class HudAssets
{
    /// <summary>
    /// Every texture (<c>Bitmap</c> and <c>Mask</c> lines, and the textures of the models found),
    /// model (<c>Mesh</c>, <c>MeshInfo</c> and the transforms' <c>NameMesh</c> rows), font
    /// (<c>TextFont</c>) and, when a <c>Text</c> line names a string, the English localization, with
    /// the path <paramref name="assets"/> finds for it. Events that name a texture or model at run
    /// time are not counted: their names come from the game. <paramref name="meshTextures"/> keeps
    /// each model file's texture names between checks.
    /// </summary>
    public static IReadOnlyList<HudAsset> Check(IReadOnlyList<HudDocument> documents, AssetResolver assets,
        IDictionary<string, IReadOnlyList<string>>? meshTextures = null)
    {
        var uses = new Dictionary<(HudAssetKind Kind, string Name), List<string>>(new KeyComparer());
        void Use(HudAssetKind kind, string? name, string by)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (!uses.TryGetValue((kind, name), out var list)) uses[(kind, name)] = list = new List<string>();
            if (!list.Contains(by)) list.Add(by);
        }

        var strings = 0;
        foreach (var doc in documents)
        {
            var file = System.IO.Path.GetFileName(doc.SourcePath) ?? "(unsaved)";
            foreach (var top in doc.TopLevel)
                Walk(top, file, file, Use, ref strings);
        }

        // The textures the models found draw with.
        foreach (var mesh in uses.Keys.Where(k => k.Kind == HudAssetKind.Mesh).Select(k => k.Name).ToList())
        {
            if (assets.FindMesh(mesh) is not { } path) continue;
            if (meshTextures is null || !meshTextures.TryGetValue(path, out var textures))
            {
                try { textures = MshParser.ExtractTextureNames(path); }
                catch { textures = Array.Empty<string>(); }
                if (meshTextures is not null) meshTextures[path] = textures;
            }
            foreach (var texture in textures)
                Use(HudAssetKind.Texture, texture.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? texture[..^4] : texture, $"model {mesh}");
        }

        var found = uses.Select(u => new HudAsset(u.Key.Kind, u.Key.Name, assets.Find(u.Key.Kind, u.Key.Name), u.Value)).ToList();

        if (strings > 0)
            found.Add(new HudAsset(HudAssetKind.Localization, "english", assets.FindLocalizationCfg("english"),
                new[] { $"{strings} Text line{(strings == 1 ? "" : "s")}" }));

        return found.OrderBy(a => a.Kind).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void Walk(HudBlock block, string owner, string file, Action<HudAssetKind, string?, string> use, ref int strings)
    {
        // What names an asset: the element or transform it is in, with its file.
        if (ElementKinds.TryGet(block, out _) || block.Name.StartsWith("Transform", StringComparison.Ordinal))
            owner = $"{block.Name} \"{StringAt(block, 0)}\" ({file})";

        foreach (var child in block.Children)
        {
            if (child.HasBody)
            {
                if (child.Name == "MeshInfo") use(HudAssetKind.Mesh, StringAt(child, 0), owner);
                Walk(child, owner, file, use, ref strings);
                continue;
            }
            switch (child.Name)
            {
                case "Bitmap":
                case "Mask":
                    use(HudAssetKind.Texture, StringAt(child, 0), owner);
                    break;
                case "Mesh":
                    use(HudAssetKind.Mesh, StringAt(child, 0), owner);
                    break;
                case "NameMesh":
                    use(HudAssetKind.Mesh, StringAt(child, 1), owner);
                    break;
                case "TextFont":
                    use(HudAssetKind.Font, StringAt(child, 0), owner);
                    break;
                case "Text" when StringAt(child, 0) is not null:
                    strings++;
                    break;
            }
        }
    }

    /// <summary>The quoted argument at <paramref name="index"/>, or null.</summary>
    private static string? StringAt(HudBlock block, int index) =>
        index < block.Arguments.Count && EngineArgs.IsString(block.Arguments[index]) ? block.Arguments[index].Raw : null;

    private sealed class KeyComparer : IEqualityComparer<(HudAssetKind Kind, string Name)>
    {
        public bool Equals((HudAssetKind Kind, string Name) a, (HudAssetKind Kind, string Name) b) =>
            a.Kind == b.Kind && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((HudAssetKind Kind, string Name) key) =>
            HashCode.Combine(key.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(key.Name));
    }
}
