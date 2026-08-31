using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PDFEditor.Services;

public class ToolbarProfile
{
    public string Name { get; set; } = "";
    /// <summary>Command IDs that should be visible in this profile. If a command isn't listed, it's hidden.</summary>
    public HashSet<string> Visible { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class ToolbarSettings
{
    public List<ToolbarProfile> Profiles { get; set; } = new();
    public string ActiveProfileName { get; set; } = "Default";
}

public class ToolbarSettingsService
{
    private readonly string _path;
    public ToolbarSettings Settings { get; private set; } = new();
    public event Action? Changed;

    public ToolbarSettingsService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ArtiMaxPDFEditor");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "toolbar.json");
        Load();
    }

    public ToolbarProfile ActiveProfile
    {
        get
        {
            var p = Settings.Profiles.Find(x => string.Equals(x.Name, Settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase));
            return p ?? Settings.Profiles[0];
        }
    }

    public bool IsVisible(string commandId) => ActiveProfile.Visible.Contains(commandId);

    public void SetActive(string profileName)
    {
        if (Settings.Profiles.Exists(p => p.Name == profileName))
        {
            Settings.ActiveProfileName = profileName;
            Save();
            Changed?.Invoke();
        }
    }

    public ToolbarProfile AddProfile(string name)
    {
        var p = new ToolbarProfile { Name = name, Visible = new HashSet<string>(ActiveProfile.Visible, StringComparer.OrdinalIgnoreCase) };
        Settings.Profiles.Add(p);
        Save();
        Changed?.Invoke();
        return p;
    }

    public void RemoveProfile(string name)
    {
        if (Settings.Profiles.Count <= 1) return; // never delete the last one
        Settings.Profiles.RemoveAll(p => p.Name == name);
        if (Settings.ActiveProfileName == name) Settings.ActiveProfileName = Settings.Profiles[0].Name;
        Save();
        Changed?.Invoke();
    }

    public void Rename(string oldName, string newName)
    {
        var p = Settings.Profiles.Find(x => x.Name == oldName);
        if (p == null) return;
        p.Name = newName;
        if (Settings.ActiveProfileName == oldName) Settings.ActiveProfileName = newName;
        Save();
        Changed?.Invoke();
    }

    public void SetVisible(string commandId, bool visible)
    {
        var p = ActiveProfile;
        if (visible) p.Visible.Add(commandId); else p.Visible.Remove(commandId);
        Save();
        Changed?.Invoke();
    }

    public void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true })); } catch { }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                Settings = JsonSerializer.Deserialize<ToolbarSettings>(File.ReadAllText(_path)) ?? new();
            }
        }
        catch { Settings = new(); }
        if (Settings.Profiles.Count == 0)
        {
            Settings.Profiles.Add(new ToolbarProfile
            {
                Name = "Default",
                Visible = new HashSet<string>(AllCommandIds, StringComparer.OrdinalIgnoreCase)
            });
            Settings.ActiveProfileName = "Default";
            Save();
        }
        else
        {
            // Migration: ensure newly-added toolbar entries appear in existing profiles.
            // Only adds — never removes — so user-hidden items stay hidden.
            var newlyAdded = new[] { "Tick", "Cross", "RectangleFilled", "EllipseFilled", "TextGroup", "ShapeGroup" };
            bool changed = false;
            foreach (var p in Settings.Profiles)
                foreach (var id in newlyAdded)
                    if (p.Visible.Add(id)) changed = true;
            if (changed) Save();
        }
    }

    /// <summary>The full list of toolbar command IDs the UI knows about. Order = default display order.</summary>
    public static readonly string[] AllCommandIds = new[]
    {
        // File
        "Open", "Save", "Print", "EditInWord", "Undo",
        // Tools
        "Select", "Highlight", "StickyNote", "TextGroup", "ShapeGroup", "Whiteout", "Erase",
        "SelectText", "SelectImage",
        // Colour block (represented as one toggle)
        "ColourSwatches",
        // Page ops
        "RotateLeft", "RotateRight", "DeletePage",
        // Insert / sign
        "InsertImage", "Signatures",
        // View
        "ZoomOut", "ZoomLevel", "ZoomIn",
        // Search
        "SearchBox", "Find",
        // Customise
        "Customise",
    };
}
