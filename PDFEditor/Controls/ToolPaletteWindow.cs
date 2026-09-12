using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using PDFEditor.ViewModels;

namespace PDFEditor.Controls;

// Small always-on-top window that mirrors every annotation tool from
// ToolCatalog as a clickable button. Bound to the same MainViewModel as
// the main window — clicks route through SetToolCommand and toggle state
// via a converter that highlights the active tool.
public class ToolPaletteWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ItemsControl _items;

    public ToolPaletteWindow(MainViewModel vm, Window owner)
    {
        _vm = vm;
        Owner = owner;
        DataContext = vm;
        // Title is intentionally short — the OS toolwindow title bar has very
        // little horizontal room, and a long title clips to "T.." when the
        // palette is in icons-only mode. The window is still identifiable via
        // its ownership + the tool buttons themselves.
        Title = "≡";
        ShowInTaskbar = false;
        WindowStyle = WindowStyle.ToolWindow;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // Sit just inside the top-right of the owner by default; user can drag.
        Left = Math.Max(0, owner.Left + owner.Width - 220);
        Top = Math.Max(0, owner.Top + 120);
        ResizeMode = ResizeMode.NoResize;
        // Pick up the app theme via DynamicResource.
        try { SetResourceReference(BackgroundProperty, "Panel"); } catch { }
        try { SetResourceReference(ForegroundProperty, "Text"); } catch { }

        // Owner relationship keeps the palette above PDF Editor content
        // (owned windows always Z-order above their owner) without floating
        // above every OTHER app on the machine. No Topmost is needed.

        // Follow the owner when it moves (drag to another monitor, snap-to-side,
        // maximize/restore). Owned windows Z-order with the owner but do NOT
        // move with it, so we track the delta and translate the palette by the
        // same amount. This preserves any manual offset the user has set by
        // dragging the palette itself.
        double lastOwnerLeft = owner.Left;
        double lastOwnerTop = owner.Top;
        owner.LocationChanged += (_, _) =>
        {
            var dx = owner.Left - lastOwnerLeft;
            var dy = owner.Top - lastOwnerTop;
            lastOwnerLeft = owner.Left;
            lastOwnerTop = owner.Top;
            if (dx == 0 && dy == 0) return;
            if (double.IsNaN(Left) || double.IsNaN(Top)) return;
            Left += dx;
            Top += dy;
        };

        var root = new DockPanel { LastChildFill = true, Margin = new Thickness(6) };

        // A thin drag strip at the top gives a comfortable grab area beyond
        // the tiny OS toolwindow title bar. Transparent so it inherits the
        // window background, but hit-testable so clicks initiate DragMove.
        var dragArea = new Border
        {
            Background = Brushes.Transparent,
            Height = 6,
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = Cursors.SizeAll
        };
        dragArea.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        };
        DockPanel.SetDock(dragArea, Dock.Top);
        root.Children.Add(dragArea);

        // Colour swatch row — mirrors the toolbar's colour picker so the user
        // can change the annotation colour without leaving the palette.
        // Bound to VM.CurrentColor via a ColorToBrush converter so both
        // surfaces stay in sync.
        var swatchButton = new Button
        {
            Cursor = Cursors.Hand,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            ToolTip = "Annotation colour — click to change"
        };
        // Button default style pins Foreground to the system control-text brush
        // (black), and a style setter beats the value inherited from the window --
        // so without this the caret glyph is black-on-dark in the dark theme.
        try { swatchButton.SetResourceReference(ForegroundProperty, "Text"); } catch { }
        swatchButton.Template = BuildFlatButtonTemplate();
        var swatchRow = new StackPanel { Orientation = Orientation.Horizontal };
        var swatchCell = new System.Windows.Shapes.Rectangle
        {
            Width = 24, Height = 24,
            VerticalAlignment = VerticalAlignment.Center,
            StrokeThickness = 1
        };
        var brushConv = Application.Current?.TryFindResource("ColorToBrush") as System.Windows.Data.IValueConverter;
        var contrastConv = Application.Current?.TryFindResource("ContrastBorder") as System.Windows.Data.IValueConverter;
        if (brushConv != null)
            swatchCell.SetBinding(System.Windows.Shapes.Rectangle.FillProperty,
                new System.Windows.Data.Binding("CurrentColor") { Converter = brushConv });
        if (contrastConv != null)
            swatchCell.SetBinding(System.Windows.Shapes.Rectangle.StrokeProperty,
                new System.Windows.Data.Binding("CurrentColor") { Converter = contrastConv });
        var caret = new TextBlock { Text = " ▾", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, Margin = new Thickness(4, 0, 0, 0) };
        swatchRow.Children.Add(swatchCell);
        swatchRow.Children.Add(caret);
        swatchButton.Content = swatchRow;
        swatchButton.Click += (_, _) => PDFEditor.Controls.ColourSwatchPopup.Show(swatchButton, _vm);
        DockPanel.SetDock(swatchButton, Dock.Top);
        root.Children.Add(swatchButton);

        _items = new ItemsControl
        {
            ItemsSource = _vm.VisiblePaletteEntries,
            ItemTemplate = BuildItemTemplate()
        };
        root.Children.Add(_items);

        Content = root;

        // Right-click anywhere on the palette background opens the Customise
        // Palette dialog. Right-click on a specific tool button bubbles up
        // here too (Buttons don't consume right-click by default), so both
        // gestures work.
        MouseRightButtonUp += (_, e) =>
        {
            PaletteCustomiseDialog.Show(_vm);
            e.Handled = true;
        };

        // React to the VM's palette-visibility changes so the ItemsSource
        // refreshes without having to close and reopen the palette.
        _vm.PropertyChanged += VmPropertyChanged;
        Closed += (_, _) => _vm.PropertyChanged -= VmPropertyChanged;
    }

    private void VmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.VisiblePaletteEntries))
        {
            _items.ItemsSource = _vm.VisiblePaletteEntries;
        }
    }

    private void EntryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.DataContext is not ToolCatalogEntry entry) return;
        if (entry.IsPlaceholder)
        {
            ShowPlaceholderRequestDialog(entry);
            return;
        }
        if (entry.Mode is ToolMode m)
        {
            _vm.SetToolCommand.Execute(m.ToString());
            return;
        }
        if (!string.IsNullOrEmpty(entry.CommandName))
        {
            var cmd = entry.CommandName switch
            {
                "InsertImageOnCurrent" => _vm.InsertImageOnCurrentCommand,
                "OpenSignatureLibrary" => _vm.OpenSignatureLibraryCommand,
                _ => (System.Windows.Input.ICommand?)null
            };
            if (cmd is not null && cmd.CanExecute(null)) cmd.Execute(null);
        }
    }

    private static void ShowPlaceholderRequestDialog(ToolCatalogEntry entry)
    {
        MessageBox.Show(
            $"“{entry.Label}” is not built yet.\n\n" +
            "It's on the shortlist as a possible future feature. If you want it, " +
            "email support@artimax.com.au (mention which feature and how you'd use it), " +
            "or file a GitHub issue at:\n" +
            "  https://github.com/MikeyBorin/PDFEditor/issues\n\n" +
            "See the Help file (F1) → \"Requesting features\" for what's known about scope.",
            "Feature not built yet",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>Flat button template matching the toolbar's ToolButton style:
    /// a Border honouring the button's own Background (so the active-tool Accent
    /// highlight still shows), with hover and pressed pulled from the Hover and
    /// Pressed theme brushes. Built in code because this window has no XAML.</summary>
    private static ControlTemplate BuildFlatButtonTemplate()
    {
        var tpl = new ControlTemplate(typeof(Button));

        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetBinding(Border.BackgroundProperty,
            new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty,
            new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);

        tpl.VisualTree = border;

        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Hover"), "Bd"));
        tpl.Triggers.Add(over);

        var pressed = new Trigger
        {
            Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty,
            Value = true
        };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Pressed"), "Bd"));
        tpl.Triggers.Add(pressed);

        return tpl;
    }

    private DataTemplate BuildItemTemplate()
    {
        // Programmatic template — a button per catalog entry, wired to
        // SetToolCommand(Mode) on the MainViewModel. Font is picked per-glyph
        // because MDL2 icons only render in Segoe MDL2 Assets and regular
        // Unicode symbols (✓ ✗ •) need Segoe UI Symbol.
        var tpl = new DataTemplate(typeof(ToolCatalogEntry));

        var btn = new FrameworkElementFactory(typeof(Button));
        btn.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);
        btn.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1));
        btn.SetValue(Control.PaddingProperty, new Thickness(6, 4, 6, 4));
        btn.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left);
        btn.SetValue(Control.BorderThicknessProperty, new Thickness(0));
        // Same trap as the swatch button, and the reason palette glyphs and labels
        // came out black in the dark theme: the tool rows are a Button template, so
        // the TextBlocks inside inherit the Button's Foreground, not the window's.
        // Pointing the Button at the theme brush fixes glyph and label in one go,
        // and keeps following the theme when it is switched at runtime.
        btn.SetResourceReference(Control.ForegroundProperty, "Text");
        // Plain Buttons keep the default Aero template, whose IsMouseOver trigger
        // paints a pale system blue -- unreadable under white theme text in the
        // dark theme. Use the same flat template the toolbar buttons use so hover
        // and pressed come from the Hover / Pressed theme brushes instead.
        btn.SetValue(Control.TemplateProperty, BuildFlatButtonTemplate());
        // Placeholder entries render at reduced opacity so they read as
        // "future / not yet implemented" without disabling clickability
        // (a click on a placeholder opens the how-to-request dialog).
        btn.SetBinding(UIElement.OpacityProperty,
            new Binding("IsPlaceholder") { Converter = new PlaceholderOpacityConverter() });
        // Click handler routes both tool modes and action entries (Image, Signature).
        btn.AddHandler(Button.ClickEvent, new RoutedEventHandler(EntryButton_Click));
        // Single-binding tooltip is more reliable than MultiBinding inside a
        // FrameworkElementFactory-built template. DisplayTooltip is a computed
        // property on ToolCatalogEntry that returns "Label — Tooltip".
        btn.SetBinding(FrameworkElement.ToolTipProperty, new Binding("DisplayTooltip"));
        btn.SetValue(ToolTipService.InitialShowDelayProperty, 200);
        btn.SetValue(ToolTipService.ShowDurationProperty, 15000);
        btn.SetValue(ToolTipService.BetweenShowDelayProperty, 0);

        // Background highlights when this row's Mode == VM.CurrentTool.
        // Uses the same EqualityToBrushConverter the toolbar buttons rely on.
        // Action rows (Mode == null) never highlight because null.ToString() → "".
        var bg = new MultiBinding
        {
            Converter = new PDFEditor.Controls.EqualityToBrushConverter
            {
                ActiveBrush = (Brush?)Application.Current?.TryFindResource("Accent") ?? Brushes.SteelBlue,
                InactiveBrush = Brushes.Transparent
            }
        };
        bg.Bindings.Add(new Binding("Mode") { Converter = new EnumToStringConverter() });
        bg.Bindings.Add(new Binding
        {
            Path = new PropertyPath("DataContext.CurrentTool"),
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Window), 1),
            Converter = new EnumToStringConverter()
        });
        btn.SetBinding(Control.BackgroundProperty, bg);

        var stack = new FrameworkElementFactory(typeof(StackPanel));
        stack.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var glyph = new FrameworkElementFactory(typeof(TextBlock));
        glyph.SetBinding(TextBlock.TextProperty, new Binding("Glyph"));
        glyph.SetValue(TextBlock.FontSizeProperty, 16.0);
        glyph.SetValue(FrameworkElement.WidthProperty, 24.0);
        glyph.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        glyph.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        // Font picked per-glyph via the shared converter (reads the code-point range).
        glyph.SetBinding(TextBlock.FontFamilyProperty, new Binding("Glyph") { Converter = new PDFEditor.Controls.GlyphFontConverter() });

        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding("Label"));
        label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        label.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
        // Hide label when the shared "icons only" setting is on. RelativeSource
        // finds the palette window whose DataContext is the MainViewModel.
        label.SetBinding(UIElement.VisibilityProperty, new Binding
        {
            Path = new PropertyPath("DataContext.ToolsIconsOnly"),
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Window), 1),
            Converter = new PDFEditor.Controls.InverseBoolToVisibilityConverter()
        });

        stack.AppendChild(glyph);
        stack.AppendChild(label);
        btn.AppendChild(stack);
        tpl.VisualTree = btn;
        return tpl;
    }

    private sealed class EnumToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value?.ToString() ?? "";
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

    private sealed class PlaceholderOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is bool b && b ? 0.5 : 1.0;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

}
