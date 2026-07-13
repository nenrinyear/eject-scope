using System.Text.Json;
using System.IO;

namespace DiskRemovalUsage.App;

public sealed class AppSettings
{
    public bool RunAtStartup { get; set; }
    public bool ShowElevateButton { get; set; } = true;
    public bool AutoScanOnRemovalFailure { get; set; }
    public bool EnableTerminateSuggestion { get; set; } = true;

    public static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskRemovalUsage", "settings.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings(); }
        catch (IOException) { return new AppSettings(); }
        catch (JsonException) { return new AppSettings(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
