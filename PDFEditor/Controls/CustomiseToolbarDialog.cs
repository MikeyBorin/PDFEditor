using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PDFEditor.Services;

namespace PDFEditor.Controls;

public static class CustomiseToolbarDialog
{
    private static readonly (string Id, string Label)[] Commands = new (string, string)[]
    {
        ("Open",         "Open"),
        ("Save",         "Save"),
        ("Print",        "Print"),
        ("EditInWord",   "Edit in Word"),
        ("Undo",         "Undo"),
        ("Select",       "Select"),
        ("Highlight",    "Highlight"),
        ("StickyNote",   "Note"),
        ("TextGroup",    "Text tools (Text · Tickmark · Crossmark · Bullet)"),
        ("ShapeGroup",   "Shape tools (Draw · Rect · Oval, outlined + filled)"),
        ("Whiteout",     "Whiteout"),
        ("Erase",        "Erase"),
        ("SelectText",   "Select text region"),
        ("SelectImage",  "Select image region"),
        ("ColourSwatches","Colour swatches"),
        ("RotateLeft",   "Rotate left"),
        ("RotateRight",  "Rotate right"),
        ("DeletePage",   "Delete page"),
        ("InsertImage",  "Insert image"),
        ("Signatures",   "Signatures"),
        ("ZoomOut",      "Zoom out"),
        ("ZoomLevel",    "Zoom level"),
        ("ZoomIn",       "Zoom in"),
        ("SearchBox",    "Search box"),
        ("Find",         "Find button"),
        ("Customise",    "Customise button"),
    };

    public static void Show(ToolbarSettingsService svc)
    {
        var w = new Window
        {
            Title = "Customise Toolbar",
            Width = 520, Height = 640,
            Owner = Application.Current?.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = System.Windows.Media.Brushes.White,
            Foreground = System.Windows.Media.Brushes.Black
        };
        var root = new DockPanel { Margin = new Thickness(12), Background = System.Windows.Media.Brushes.White };

        // Top: profile picker + actions
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var profileLabel = new TextBlock { Text = "Profile:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Foreground = System.Windows.Media.Brushes.Black };
        var profileBox = new ComboBox { MinWidth = 160, Margin = new Thickness(0, 0, 6, 0) };
        var newBtn = new Button { Content = "New", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        var renBtn = new Button { Content = "Rename", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        var delBtn = new Button { Content = "Delete", Padding = new Thickness(10, 4, 10, 4) };
        top.Children.Add(profileLabel); top.Children.Add(profileBox);
        top.Children.Add(newBtn); top.Children.Add(renBtn); top.Children.Add(delBtn);
        DockPanel.SetDock(top, Dock.Top);

        // Bottom: close
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var close = new Button { Content = "Close", Width = 90, Height = 28, IsCancel = true, IsDefault = true };
        bottom.Children.Add(close);
        DockPanel.SetDock(bottom, Dock.Bottom);

        // Middle: checkboxes
        var stack = new StackPanel { Background = System.Windows.Media.Brushes.White };
        var scroll = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = System.Windows.Media.Brushes.White
        };

        var boxes = new Dictionary<string, CheckBox>();
        foreach (var (id, label) in Commands)
        {
            var cb = new CheckBox
            {
                Content = label,
                Margin = new Thickness(0, 4, 0, 4),
                Tag = id,
                Foreground = System.Windows.Media.Brushes.Black,
                Background = System.Windows.Media.Brushes.White
            };
            cb.Checked += (_, _) => svc.SetVisible(id, true);
            cb.Unchecked += (_, _) => svc.SetVisible(id, false);
            boxes[id] = cb;
            stack.Children.Add(cb);
        }

        void RefreshChecks()
        {
            foreach (var (id, _) in Commands) boxes[id].IsChecked = svc.IsVisible(id);
        }
        void RefreshProfiles()
        {
            profileBox.Items.Clear();
            foreach (var p in svc.Settings.Profiles) profileBox.Items.Add(p.Name);
            profileBox.SelectedItem = svc.Settings.ActiveProfileName;
        }
        RefreshProfiles();
        RefreshChecks();

        profileBox.SelectionChanged += (_, _) =>
        {
            if (profileBox.SelectedItem is string name)
            {
                svc.SetActive(name);
                RefreshChecks();
            }
        };
        newBtn.Click += (_, _) =>
        {
            var name = PromptDialog.Ask("New profile", "Profile name:", "Custom");
            if (string.IsNullOrWhiteSpace(name)) return;
            svc.AddProfile(name);
            svc.SetActive(name);
            RefreshProfiles();
            RefreshChecks();
        };
        renBtn.Click += (_, _) =>
        {
            var name = PromptDialog.Ask("Rename profile", "New name:", svc.Settings.ActiveProfileName);
            if (string.IsNullOrWhiteSpace(name)) return;
            svc.Rename(svc.Settings.ActiveProfileName, name);
            RefreshProfiles();
        };
        delBtn.Click += (_, _) =>
        {
            if (svc.Settings.Profiles.Count <= 1)
            {
                MessageBox.Show("Cannot delete the last profile.", "Customise", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show($"Delete profile '{svc.Settings.ActiveProfileName}'?", "Confirm", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            svc.RemoveProfile(svc.Settings.ActiveProfileName);
            RefreshProfiles();
            RefreshChecks();
        };

        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(scroll);
        w.Content = root;
        w.ShowDialog();
    }
}
