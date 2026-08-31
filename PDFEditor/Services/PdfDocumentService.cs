using System;
using System.IO;
using System.Linq;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace PDFEditor.Services;

/// <summary>Owns the currently-loaded PDF bytes and metadata. Everything downstream reads from Bytes.</summary>
public class PdfDocumentService
{
    public byte[]? Bytes { get; private set; }
    public string? FilePath { get; private set; }
    public int PageCount { get; private set; }
    public bool IsDirty { get; set; }

    /// <summary>Public view of one operation in the undo history.</summary>
    public record HistoryEntry(string Label, DateTime When);

    /// <summary>Undo history: byte snapshots of past states with a human label.</summary>
    private record Frame(byte[] Bytes, string Label, DateTime When);

    private readonly System.Collections.Generic.Stack<Frame> _undo = new();
    private const int MaxUndo = 20;
    public bool CanUndo => _undo.Count > 0;

    /// <summary>History ordered most-recent-first (index 0 = would be undone by the next Ctrl+Z).</summary>
    public System.Collections.Generic.IReadOnlyList<HistoryEntry> History =>
        _undo.Select(f => new HistoryEntry(f.Label, f.When)).ToList();

    /// <summary>Peek at the bytes that a plain <see cref="Undo"/> would restore, without popping.
    /// Useful for "smart delete" flows that want to check whether they can revert cleanly to a
    /// known previous state instead of doing a destructive rewrite.</summary>
    public byte[]? PeekLastUndoBytes() => _undo.Count > 0 ? _undo.Peek().Bytes : null;

    /// <summary>Held file handle that prevents other processes from modifying/deleting the file while it's open.</summary>
    private FileStream? _lock;

    public event Action? DocumentChanged;

    public void Load(string path)
    {
        ReleaseLock();
        // Open with FileShare.Read so others can read but not write/delete.
        _lock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = new byte[_lock.Length];
        int read = 0;
        while (read < bytes.Length)
        {
            var n = _lock.Read(bytes, read, bytes.Length - read);
            if (n == 0) break;
            read += n;
        }
        LoadFromBytes(bytes, path);
        // Keep _lock open — it holds the FileShare.Read lock.
    }

    private void ReleaseLock()
    {
        try { _lock?.Dispose(); } catch { }
        _lock = null;
    }

    public void LoadFromBytes(byte[] bytes, string? path = null)
    {
        Bytes = bytes;
        FilePath = path;
        using var ms = new MemoryStream(bytes);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
        PageCount = doc.PageCount;
        IsDirty = false;
        DocumentChanged?.Invoke();
    }

    public void Close()
    {
        ReleaseLock();
        Bytes = null;
        FilePath = null;
        PageCount = 0;
        IsDirty = false;
        ClearUndo();
        DocumentChanged?.Invoke();
    }

    public PdfDocument OpenForEdit()
    {
        if (Bytes is null) throw new InvalidOperationException("No document loaded.");
        var ms = new MemoryStream(Bytes);
        return PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
    }

    public void ReplaceBytes(byte[] newBytes, string label = "Edit", bool markDirty = true, bool pushUndo = true)
    {
        if (pushUndo && Bytes != null)
        {
            _undo.Push(new Frame(Bytes, label, DateTime.Now));
            // Trim old entries by popping the bottom (rebuild stack).
            if (_undo.Count > MaxUndo)
            {
                var keep = _undo.ToArray().Take(MaxUndo).Reverse().ToArray();
                _undo.Clear();
                foreach (var f in keep) _undo.Push(f);
            }
        }
        Bytes = newBytes;
        using var ms = new MemoryStream(newBytes);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
        PageCount = doc.PageCount;
        IsDirty = markDirty;
        DocumentChanged?.Invoke();
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var previous = _undo.Pop();
        // Restore without pushing again.
        ReplaceBytes(previous.Bytes, label: "Undo", markDirty: true, pushUndo: false);
        return true;
    }

    /// <summary>Undo everything from the top of the stack down to and including the entry
    /// at <paramref name="historyIndex"/>. Effectively "revert to before this operation".</summary>
    public bool RevertToBefore(int historyIndex)
    {
        if (historyIndex < 0 || historyIndex >= _undo.Count) return false;
        for (int i = 0; i <= historyIndex; i++)
        {
            if (!Undo()) return false;
        }
        return true;
    }

    public void ClearUndo() => _undo.Clear();

    public void Save(string path)
    {
        if (Bytes is null) throw new InvalidOperationException("No document loaded.");
        // Release the read lock before writing (Windows won't let us overwrite our own held handle).
        var wasSameFile = string.Equals(path, FilePath, StringComparison.OrdinalIgnoreCase);
        if (wasSameFile) ReleaseLock();
        File.WriteAllBytes(path, Bytes);
        FilePath = path;
        IsDirty = false;
        // Re-acquire the lock on the freshly-written file.
        _lock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}
