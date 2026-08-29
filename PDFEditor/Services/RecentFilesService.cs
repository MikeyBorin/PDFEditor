using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PDFEditor.Services;

public class RecentFilesService
{
    private const int MaxItems = 10;
    private readonly string _path;
    public List<string> Files { get; private set; } = new();

    public event Action? Changed;

    public RecentFilesService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PDFEditor");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "recent.json");
        Load();
    }

    public void Add(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        Files.RemoveAll(f => string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));
        Files.Insert(0, filePath);
        if (Files.Count > MaxItems) Files = Files.Take(MaxItems).ToList();
        Save();
        Changed?.Invoke();
    }

    public void Remove(string filePath)
    {
        var removed = Files.RemoveAll(f => string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));
        if (removed > 0) { Save(); Changed?.Invoke(); }
    }

    public void Clear()
    {
        Files.Clear();
        Save();
        Changed?.Invoke();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var list = JsonSerializer.Deserialize<List<string>>(json);
            if (list != null) Files = list.Where(File.Exists).Take(MaxItems).ToList();
        }
        catch { Files = new(); }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Files);
            File.WriteAllText(_path, json);
        }
        catch { /* best-effort */ }
    }
}
