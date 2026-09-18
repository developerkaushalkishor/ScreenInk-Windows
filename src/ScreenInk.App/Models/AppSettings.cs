using System.Text.Json;
using ScreenInk.Core;

namespace ScreenInk.App.Models;

public sealed class AppSettings
{
    public bool IsEnabled { get; set; } = true;
    public bool AutoHideToolbar { get; set; } = true;
    public uint Color { get; set; } = 0xFFBF5AF2;
    public double Width { get; set; } = 4;
    public DrawingTool Tool { get; set; } = DrawingTool.Pen;
    public bool CursorHalo { get; set; }
    public bool ClickAnimations { get; set; }
    public double FadeDelay { get; set; } = 5;
    public string FontFamily { get; set; } = "Segoe Print";
    public double FontSize { get; set; } = 28;
    public InkTextAlignment TextAlignment { get; set; }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScreenInk", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new()
                : new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
