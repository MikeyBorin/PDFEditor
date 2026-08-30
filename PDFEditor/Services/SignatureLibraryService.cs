using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PDFEditor.Services;

public class SignatureEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string FileName { get; set; } = ""; // relative to library dir
    public DateTime Added { get; set; } = DateTime.Now;
}

public class SignatureLibraryService
{
    public string LibraryDir { get; }
    private string IndexPath => Path.Combine(LibraryDir, "index.json");

    public SignatureLibraryService()
    {
        LibraryDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ArtiMaxPDFEditor", "signatures");
        Directory.CreateDirectory(LibraryDir);
    }

    public List<SignatureEntry> List()
    {
        try
        {
            if (!File.Exists(IndexPath)) return new();
            var list = JsonSerializer.Deserialize<List<SignatureEntry>>(File.ReadAllText(IndexPath)) ?? new();
            // Drop entries whose backing file has been deleted.
            list = list.Where(e => File.Exists(Path.Combine(LibraryDir, e.FileName))).ToList();
            return list.OrderByDescending(e => e.Added).ToList();
        }
        catch { return new(); }
    }

    public string GetFullPath(SignatureEntry e) => Path.Combine(LibraryDir, e.FileName);

    /// <summary>Copies a source image into the library and returns the new entry.</summary>
    public SignatureEntry AddFromFile(string sourcePath, string? displayName = null)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif"))
            throw new InvalidOperationException("Unsupported image type: " + ext);
        var entry = new SignatureEntry
        {
            Name = displayName ?? Path.GetFileNameWithoutExtension(sourcePath),
            FileName = $"sig-{Guid.NewGuid():N}{ext}"
        };
        File.Copy(sourcePath, Path.Combine(LibraryDir, entry.FileName), true);
        var list = List();
        list.Insert(0, entry);
        Save(list);
        return entry;
    }

    /// <summary>Copies raw PNG bytes (e.g. from the draw dialog) into the library.</summary>
    public SignatureEntry AddFromPngBytes(byte[] png, string displayName)
    {
        var entry = new SignatureEntry
        {
            Name = displayName,
            FileName = $"sig-{Guid.NewGuid():N}.png"
        };
        File.WriteAllBytes(Path.Combine(LibraryDir, entry.FileName), png);
        var list = List();
        list.Insert(0, entry);
        Save(list);
        return entry;
    }

    public void Remove(string id)
    {
        var list = List();
        var e = list.FirstOrDefault(x => x.Id == id);
        if (e == null) return;
        var path = Path.Combine(LibraryDir, e.FileName);
        try { if (File.Exists(path)) File.Delete(path); } catch { }
        list.RemoveAll(x => x.Id == id);
        Save(list);
    }

    public void Rename(string id, string newName)
    {
        var list = List();
        var e = list.FirstOrDefault(x => x.Id == id);
        if (e == null) return;
        e.Name = newName;
        Save(list);
    }

    private void Save(List<SignatureEntry> list)
    {
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }
}
