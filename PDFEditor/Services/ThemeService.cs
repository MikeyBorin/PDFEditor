using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace PDFEditor.Services;

public enum AppTheme { Light, Dark }

/// <summary>
/// Runtime theme swap: keeps references to named SolidColorBrush resources in App.Resources
/// and updates their .Color when the theme changes. Because brushes are shared instances,
/// every consumer re-renders.
/// </summary>
public class ThemeService
{
    private readonly string _settingsPath;
    public AppTheme Current { get; private set; } = AppTheme.Dark;
    public event Action? Changed;

    public ThemeService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PDFEditor");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "theme.json");
        try
        {
            if (File.Exists(_settingsPath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsPath));
                if (s != null) Current = s.Theme;
            }
        }
        catch { }
    }

    private record Settings(AppTheme Theme);

    public void Apply(AppTheme theme)
    {
        Current = theme;
        var res = Application.Current?.Resources;
        if (res is null) return;

        var palette = theme == AppTheme.Dark ? DarkPalette : LightPalette;
        foreach (var (key, color) in palette)
        {
            // Always replace the resource entry — DynamicResource consumers refresh automatically.
            res[key] = new SolidColorBrush(color);
        }

        try { File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new Settings(theme))); } catch { }
        Changed?.Invoke();
    }

    private static readonly (string Key, Color Color)[] DarkPalette = new[]
    {
        ("Bg",             Color.FromRgb(0x1E, 0x1F, 0x22)),
        ("Panel",          Color.FromRgb(0x2B, 0x2D, 0x31)),
        ("PanelAlt",       Color.FromRgb(0x23, 0x24, 0x28)),
        ("Border",         Color.FromRgb(0x3B, 0x3D, 0x42)),
        ("Text",           Color.FromRgb(0xE6, 0xE6, 0xE6)),
        ("TextMuted",      Color.FromRgb(0x9A, 0xA0, 0xA6)),
        ("Accent",         Color.FromRgb(0x4C, 0x8B, 0xF5)),
        ("AccentHover",    Color.FromRgb(0x6B, 0xA0, 0xFF)),
        ("InputBg",        Color.FromRgb(0x1A, 0x1B, 0x1E)),
        ("InputFg",        Color.FromRgb(0xE6, 0xE6, 0xE6)),
        // Hover / pressed / selection states — always distinct from Panel and safe with Text.
        ("Hover",          Color.FromRgb(0x3A, 0x3C, 0x42)),
        ("Pressed",        Color.FromRgb(0x4A, 0x4C, 0x52)),
        ("Selection",      Color.FromRgb(0x4C, 0x8B, 0xF5)),
        ("SelectionText",  Color.FromRgb(0xFF, 0xFF, 0xFF)),
        // Menu popup surface (used by MenuItem custom template).
        ("MenuPopupBg",    Color.FromRgb(0x24, 0x26, 0x2A)),
        ("MenuPopupFg",    Color.FromRgb(0xE6, 0xE6, 0xE6)),
    };

    private static readonly (string Key, Color Color)[] LightPalette = new[]
    {
        ("Bg",             Color.FromRgb(0xF5, 0xF6, 0xF8)),
        ("Panel",          Color.FromRgb(0xE8, 0xEA, 0xEE)),
        ("PanelAlt",       Color.FromRgb(0xFA, 0xFB, 0xFC)),
        ("Border",         Color.FromRgb(0xC5, 0xC8, 0xCE)),
        ("Text",           Color.FromRgb(0x14, 0x16, 0x1A)),
        ("TextMuted",      Color.FromRgb(0x55, 0x5A, 0x63)),
        ("Accent",         Color.FromRgb(0x1E, 0x66, 0xD5)),
        ("AccentHover",    Color.FromRgb(0x3B, 0x83, 0xF0)),
        ("InputBg",        Color.FromRgb(0xFF, 0xFF, 0xFF)),
        ("InputFg",        Color.FromRgb(0x14, 0x16, 0x1A)),
        ("Hover",          Color.FromRgb(0xD6, 0xDD, 0xEB)),
        ("Pressed",        Color.FromRgb(0xB8, 0xC5, 0xDF)),
        ("Selection",      Color.FromRgb(0x1E, 0x66, 0xD5)),
        ("SelectionText",  Color.FromRgb(0xFF, 0xFF, 0xFF)),
        ("MenuPopupBg",    Color.FromRgb(0xFF, 0xFF, 0xFF)),
        ("MenuPopupFg",    Color.FromRgb(0x14, 0x16, 0x1A)),
    };
}
