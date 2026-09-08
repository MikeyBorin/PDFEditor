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
        MigrateOpaqueImages();
    }

    // One-time migration: any signature whose backing file is a photo format
    // (JPG/BMP/GIF, or a legacy opaque PNG import) is re-processed through the
    // white-to-alpha filter and saved as a transparent PNG. Existing drawn
    // signatures (already transparent PNGs) are left alone.
    private void MigrateOpaqueImages()
    {
        try
        {
            if (!File.Exists(IndexPath)) return;
            var list = JsonSerializer.Deserialize<List<SignatureEntry>>(File.ReadAllText(IndexPath)) ?? new();
            var changed = false;
            foreach (var e in list)
            {
                var full = Path.Combine(LibraryDir, e.FileName);
                if (!File.Exists(full)) continue;
                var ext = Path.GetExtension(e.FileName).ToLowerInvariant();
                if (ext is ".jpg" or ".jpeg" or ".bmp" or ".gif")
                {
                    try
                    {
                        var pngBytes = SignatureImageProcessor.WhiteToAlphaPngFromFile(full);
                        var newFile = $"sig-{Guid.NewGuid():N}.png";
                        File.WriteAllBytes(Path.Combine(LibraryDir, newFile), pngBytes);
                        try { File.Delete(full); } catch { }
                        e.FileName = newFile;
                        changed = true;
                    }
                    catch { /* leave entry as-is if a single file fails */ }
                }
            }
            if (changed) Save(list);
        }
        catch { /* migration is best-effort; do not throw during startup */ }
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

    /// <summary>Imports a source image, converts white/near-white pixels to
    /// transparent, and saves the result as a PNG in the library.</summary>
    public SignatureEntry AddFromFile(string sourcePath, string? displayName = null)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif"))
            throw new InvalidOperationException("Unsupported image type: " + ext);
        // Always run through white-to-alpha and store as PNG so downstream
        // rendering (WPF overlay + PdfSharpCore flatten) shows the signature
        // strokes over the page content, not on an opaque white background.
        var pngBytes = SignatureImageProcessor.WhiteToAlphaPngFromFile(sourcePath);
        var entry = new SignatureEntry
        {
            Name = displayName ?? Path.GetFileNameWithoutExtension(sourcePath),
            FileName = $"sig-{Guid.NewGuid():N}.png"
        };
        File.WriteAllBytes(Path.Combine(LibraryDir, entry.FileName), pngBytes);
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
