using System;
using System.IO;
using System.Text.Json;
using PDFEditor.ViewModels;

namespace PDFEditor.Services;

public class ViewSettings
{
    public ZoomMode ZoomMode { get; set; } = ZoomMode.FitWidth;
    /// <summary>Only used when <see cref="ZoomMode"/> is <see cref="ZoomMode.Custom"/>. 1.0 = 100%.</summary>
    public double CustomZoom { get; set; } = 1.0;

    /// <summary>Last-used annotation colour (hex "#RRGGBB"). Persisted so the
    /// swatch in the toolbar reopens on next launch showing the colour the user
    /// finished with.</summary>
    public string CurrentColorHex { get; set; } = "#000000";

    /// <summary>Tesseract language code for OCR (e.g. "eng", "fra"). Used by
    /// Run OCR / OCR All Pages / Make Searchable PDF. User picks from the
    /// "OCR Languages" submenu — installing a language and setting it as
    /// current in one click.</summary>
    public string OcrLanguage { get; set; } = "eng";
}

/// <summary>Persists user's zoom-mode preference across app runs. JSON in
/// %APPDATA%\ArtiMaxPDFEditor\view.json.</summary>
public class ViewSettingsService
{
    private readonly string _path;
    public ViewSettings Settings { get; private set; } = new();

    public ViewSettingsService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ArtiMaxPDFEditor");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "view.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
                Settings = JsonSerializer.Deserialize<ViewSettings>(File.ReadAllText(_path)) ?? new();
        }
        catch { Settings = new(); }
    }

    public void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }
}
