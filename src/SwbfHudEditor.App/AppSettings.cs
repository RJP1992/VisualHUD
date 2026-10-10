using System.IO;
using System.Text.Json;

namespace SwbfHudEditor.App;

/// <summary>The editor's settings, kept per user in <c>%LOCALAPPDATA%\VisualHUD\settings.json</c>.</summary>
internal sealed class AppSettings
{
    /// <summary>
    /// Folders searched, with all their subfolders, for the assets a HUD's own folders and the stock
    /// ones do not have.
    /// </summary>
    public List<string> AssetFolders { get; set; } = new();

    /// <summary>The reference image laid under the HUD, if one is loaded, and how it is shown.</summary>
    public string? ReferenceImage { get; set; }
    public double ReferenceOpacity { get; set; } = 0.5;
    public bool ReferenceOnTop { get; set; }
    public bool ReferenceShown { get; set; } = true;

    /// <summary>The settings file: <c>VISUALHUD_SETTINGS</c> if set, as the tests do, else the user's.</summary>
    internal static string FilePath =>
        Environment.GetEnvironmentVariable("VISUALHUD_SETTINGS") is { Length: > 0 } path
            ? path
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisualHUD", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // A settings file that cannot be read starts over.
        }
        return new AppSettings();
    }

    /// <summary>Write the settings; the error, if they could not be written.</summary>
    public string? Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }
}
