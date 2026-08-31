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
