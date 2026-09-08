using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PDFEditor.Controls;

public static class TextStampDialog
{
    /// <summary><see cref="BackgroundHex"/> null/empty means "no background"
    /// (transparent). <see cref="FontWeight"/> is an OpenType-style weight
    /// (400 Regular / 500 Medium / 600 SemiBold / 700 Bold).</summary>
    public record Result(string Text, string FontFamily, double FontSize, int FontWeight, bool Italic, bool Underline, string ColorHex, PDFEditor.Models.TextAlign Align, string? BackgroundHex,
        bool Strikethrough = false, bool DoubleStrikethrough = false,
        bool Superscript = false, bool Subscript = false,
        bool SmallCaps = false, bool AllCaps = false);

    public static Result? Show(string defaultText = "", string defaultFont = "Arial",
                                double defaultSize = 14, int defaultFontWeight = 400,
                                bool defaultItalic = false, bool defaultUnderline = false,
                                string defaultColorHex = "#000000",
                                PDFEditor.Models.TextAlign defaultAlign = PDFEditor.Models.TextAlign.Left,
                                string? defaultBackgroundHex = null,
                                bool defaultStrikethrough = false,
                                bool defaultDoubleStrikethrough = false,
                                bool defaultSuperscript = false,
                                bool defaultSubscript = false,
                                bool defaultSmallCaps = false,
                                bool defaultAllCaps = false)
    {
        var w = new Window
        {
            Title = "Text",
            Width = 560,
            Height = 560,
            Owner = Application.Current?.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false,
            MinWidth = 500, MinHeight = 440
            // No SizeToContent: with SizeToContent.Height, WPF resizes the window
            // AFTER initial layout, and any ComboBox popup opened inside can capture
            // stale coordinates and misposition to the top-left corner.
        };
        var root = new Grid { Margin = new Thickness(16) };
        // Rows 0..6 = Auto (labels, controls, effects), 7 = Star (preview), 8 = Auto (buttons).
        for (int i = 0; i < 7; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Row 0: Text label + input
        root.Children.Add(WithRow(new TextBlock { Text = "Text:", Margin = new Thickness(0, 0, 0, 4) }, 0));
        var text = new TextBox
        {
            Text = defaultText,
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.Wrap,
            MinLines = 1,
            MaxLines = 20,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        root.Children.Add(WithRow(text, 1));

        // Row 2: labels for font/size/color
        var labels = new Grid { Margin = new Thickness(0, 12, 0, 4) };
        labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        labels.Children.Add(WithCol(new TextBlock { Text = "Font family", Margin = new Thickness(0, 0, 8, 0) }, 0));
        labels.Children.Add(WithCol(new TextBlock { Text = "Size (pt)", Margin = new Thickness(0, 0, 8, 0) }, 1));
        labels.Children.Add(WithCol(new TextBlock { Text = "Colour" }, 2));
        root.Children.Add(WithRow(labels, 2));

        // Row 3: font family (all installed), size, color group
        var controls = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var font = new ComboBox { IsEditable = true, Margin = new Thickness(0, 0, 8, 0), MinHeight = 24 };
        foreach (var f in GetAllFontFamilyNames()) font.Items.Add(f);
        font.SelectedItem = defaultFont;
        if (font.SelectedItem == null) font.Text = defaultFont;
        controls.Children.Add(WithCol(font, 0));

        var size = new ComboBox { IsEditable = true, Margin = new Thickness(0, 0, 8, 0), MinHeight = 24 };
        foreach (var s in new[] { 8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72 })
            size.Items.Add(s.ToString());
        size.Text = defaultSize.ToString("0");
        controls.Children.Add(WithCol(size, 1));

        var colorGroup = new Grid();
        colorGroup.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        colorGroup.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        colorGroup.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        var swatch = new System.Windows.Shapes.Rectangle
        {
            Width = 24, Height = 24,
            Stroke = System.Windows.Media.Brushes.Gray,
            StrokeThickness = 1,
            Fill = TryParseBrush(defaultColorHex),
            Margin = new Thickness(0, 0, 6, 0)
        };
        colorGroup.Children.Add(WithCol(swatch, 0));
        var colorHex = new TextBox { Text = defaultColorHex, MinHeight = 24 };
        colorGroup.Children.Add(WithCol(colorHex, 1));
        var pick = new Button { Content = "Pick...", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 2, 6, 2) };
        colorGroup.Children.Add(WithCol(pick, 2));
        controls.Children.Add(WithCol(colorGroup, 2));

        root.Children.Add(WithRow(controls, 3));

        // Row 4: background colour (label + swatch + hex + Pick + None).
        // BackgroundHex is nullable — leave the field empty (or click None) to
        // render with no background (transparent).
        var bgRow = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        bgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });     // label
        bgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });     // swatch
        bgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // hex box
        bgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });     // Pick
        bgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });     // None
        bgRow.Children.Add(WithCol(new TextBlock { Text = "Background", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }, 0));
        var bgSwatch = new System.Windows.Shapes.Rectangle
        {
            Width = 24, Height = 24,
            Stroke = System.Windows.Media.Brushes.Gray,
            StrokeThickness = 1,
            Fill = string.IsNullOrEmpty(defaultBackgroundHex) ? Brushes.Transparent : TryParseBrush(defaultBackgroundHex!),
            Margin = new Thickness(0, 0, 6, 0)
        };
        bgRow.Children.Add(WithCol(bgSwatch, 1));
        var bgHex = new TextBox { Text = defaultBackgroundHex ?? "", MinHeight = 24 };
        bgRow.Children.Add(WithCol(bgHex, 2));
        var bgPick = new Button { Content = "Pick...", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 2, 6, 2) };
        bgRow.Children.Add(WithCol(bgPick, 3));
        var bgNone = new Button { Content = "None", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 2, 6, 2), ToolTip = "Clear the background (transparent)." };
        bgRow.Children.Add(WithCol(bgNone, 4));
        root.Children.Add(WithRow(bgRow, 4));

        // Row 5: weight / italic / underline / align
        var style = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var weightLabel = new TextBlock { Text = "Weight:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        var weightBox = new ComboBox
        {
            MinWidth = 110,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
            ToolTip = "OpenType-style font weight. Medium / SemiBold flatten to Bold on save."
        };
        var weightOptions = new (string Label, int Value)[]
        {
            ("Regular",  400),
            ("Medium",   500),
            ("SemiBold", 600),
            ("Bold",     700),
        };
        foreach (var (label, _) in weightOptions) weightBox.Items.Add(label);
        var initialWeightLabel = weightOptions.MinBy(o => System.Math.Abs(o.Value - defaultFontWeight)).Label;
        weightBox.SelectedItem = initialWeightLabel;

        var italic = new CheckBox { Content = "Italic", IsChecked = defaultItalic, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        var underline = new CheckBox { Content = "Underline", IsChecked = defaultUnderline, Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
        style.Children.Add(weightLabel);
        style.Children.Add(weightBox);
        style.Children.Add(italic);
        style.Children.Add(underline);

        int SelectedFontWeight()
        {
            var label = weightBox.SelectedItem as string ?? "Regular";
            foreach (var (l, v) in weightOptions) if (l == label) return v;
            return 400;
        }

        var alignLabel = new TextBlock { Text = "Align:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) };
        var alignBox = new ComboBox { MinWidth = 90, VerticalAlignment = VerticalAlignment.Center };
        foreach (var name in new[] { "Left", "Center", "Right", "Justify" }) alignBox.Items.Add(name);
        alignBox.SelectedItem = defaultAlign.ToString();
        style.Children.Add(alignLabel);
        style.Children.Add(alignBox);
        root.Children.Add(WithRow(style, 5));

        // Row 6: Word-style Effects — checkboxes for strikethrough / super/subscript / caps.
        // Sub/Super are mutually exclusive; SmallCaps/AllCaps are mutually exclusive.
        // The UI enforces that by clearing the paired flag when one is turned on.
        var effectsBlock = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 6, 0, 0) };
        var effectsLabel = new TextBlock { Text = "Effects:", Margin = new Thickness(0, 0, 0, 4), Foreground = System.Windows.Media.Brushes.Gray };
        effectsBlock.Children.Add(effectsLabel);
        var effectsRow = new StackPanel { Orientation = Orientation.Horizontal };
        var strike = new CheckBox { Content = "Strikethrough",       IsChecked = defaultStrikethrough,       Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var strike2 = new CheckBox { Content = "Double Strikethrough", IsChecked = defaultDoubleStrikethrough, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var superS = new CheckBox { Content = "Superscript",         IsChecked = defaultSuperscript,         Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var subS   = new CheckBox { Content = "Subscript",           IsChecked = defaultSubscript,           Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var small  = new CheckBox { Content = "Small Caps",          IsChecked = defaultSmallCaps,           Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var allc   = new CheckBox { Content = "All Caps",            IsChecked = defaultAllCaps,             Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        effectsRow.Children.Add(strike);
        effectsRow.Children.Add(strike2);
        effectsRow.Children.Add(superS);
        effectsRow.Children.Add(subS);
        effectsRow.Children.Add(small);
        effectsRow.Children.Add(allc);
        effectsBlock.Children.Add(effectsRow);
        root.Children.Add(WithRow(effectsBlock, 6));

        // Row 7: preview area
        var previewBox = new Border
        {
            BorderBrush = System.Windows.Media.Brushes.LightGray, BorderThickness = new Thickness(1),
            Background = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 12, 0, 0),
            MinHeight = 60
        };
        var previewText = new TextBlock
        {
            Text = string.IsNullOrEmpty(defaultText) ? "Preview" : defaultText,
            Padding = new Thickness(8),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        previewBox.Child = previewText;
        root.Children.Add(WithRow(previewBox, 7));

        // Row 8: buttons
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", Width = 80, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, Height = 28, IsCancel = true };
        btns.Children.Add(ok); btns.Children.Add(cancel);
        root.Children.Add(WithRow(btns, 8));

        w.Content = root;

        void UpdatePreview()
        {
            try { previewText.Foreground = TryParseBrush(colorHex.Text); } catch { }
            try { previewText.FontFamily = new FontFamily((font.SelectedItem as string) ?? font.Text ?? "Arial"); } catch { }
            var sizeText = size.Text;
            if (string.IsNullOrEmpty(sizeText) && size.SelectedItem is string ss) sizeText = ss;
            double baseSize = 14;
            if (double.TryParse(sizeText, out var sz) && sz > 0) baseSize = sz;

            // Super/subscript shrinks the type and shifts baseline via Padding.
            bool sup = superS.IsChecked == true;
            bool sub = subS.IsChecked == true;
            double effSize = (sup || sub) ? System.Math.Max(6, baseSize * 0.7) : baseSize;
            previewText.FontSize = effSize;
            var basePad = new Thickness(8);
            if (sup) previewText.Padding = new Thickness(basePad.Left, System.Math.Max(0, basePad.Top - baseSize * 0.35), basePad.Right, basePad.Bottom + baseSize * 0.35);
            else if (sub) previewText.Padding = new Thickness(basePad.Left, basePad.Top + baseSize * 0.35, basePad.Right, System.Math.Max(0, basePad.Bottom - baseSize * 0.35));
            else previewText.Padding = basePad;

            previewText.FontWeight = FontWeight.FromOpenTypeWeight(SelectedFontWeight());
            previewText.FontStyle = italic.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
            // Combine decorations: Underline + Strikethrough (Double approximates as
            // Strikethrough — WPF doesn't have a native double-strike glyph, and
            // rendering a real doubled line requires custom drawing which the
            // flatten path handles, so the preview shows single).
            var decos = new TextDecorationCollection();
            if (underline.IsChecked == true) decos.Add(TextDecorations.Underline);
            if (strike2.IsChecked == true)
            {
                // Two parallel strike lines to preview Double Strikethrough properly.
                decos.Add(new TextDecoration
                {
                    Location = TextDecorationLocation.Strikethrough,
                    PenOffset = 1.5,
                    PenOffsetUnit = TextDecorationUnit.Pixel
                });
                decos.Add(new TextDecoration
                {
                    Location = TextDecorationLocation.Strikethrough,
                    PenOffset = -1.5,
                    PenOffsetUnit = TextDecorationUnit.Pixel
                });
            }
            else if (strike.IsChecked == true)
            {
                decos.Add(TextDecorations.Strikethrough);
            }
            previewText.TextDecorations = decos;
            previewText.TextAlignment = (alignBox.SelectedItem as string) switch
            {
                "Center" => TextAlignment.Center,
                "Right" => TextAlignment.Right,
                "Justify" => TextAlignment.Justify,
                _ => TextAlignment.Left
            };
            // Apply caps: AllCaps + SmallCaps both fold to uppercase for the WPF preview;
            // the PDF flatten path renders SmallCaps as small-uppercase properly (see AnnotationService).
            var raw = string.IsNullOrEmpty(text.Text) ? "Preview" : text.Text;
            if (allc.IsChecked == true || small.IsChecked == true) raw = raw.ToUpper();
            previewText.Text = raw;
            swatch.Fill = TryParseBrush(colorHex.Text);
            // Background: empty hex = transparent (preview shows white behind).
            if (string.IsNullOrWhiteSpace(bgHex.Text))
            {
                bgSwatch.Fill = Brushes.Transparent;
                previewBox.Background = Brushes.White;
            }
            else
            {
                var bgBrush = TryParseBrush(bgHex.Text);
                bgSwatch.Fill = bgBrush;
                previewBox.Background = bgBrush;
            }
        }

        text.TextChanged += (_, _) => UpdatePreview();
        colorHex.TextChanged += (_, _) => UpdatePreview();
        bgHex.TextChanged += (_, _) => UpdatePreview();
        size.SelectionChanged += (_, _) => UpdatePreview();
        size.LostFocus += (_, _) => UpdatePreview();
        size.KeyUp += (_, _) => UpdatePreview();
        font.SelectionChanged += (_, _) => UpdatePreview();
        font.LostFocus += (_, _) => UpdatePreview();
        weightBox.SelectionChanged += (_, _) => UpdatePreview();
        italic.Checked += (_, _) => UpdatePreview(); italic.Unchecked += (_, _) => UpdatePreview();
        underline.Checked += (_, _) => UpdatePreview(); underline.Unchecked += (_, _) => UpdatePreview();
        alignBox.SelectionChanged += (_, _) => UpdatePreview();

        // Effects: mutually exclusive within each pair.
        strike.Checked  += (_, _) => { if (strike2.IsChecked == true) strike2.IsChecked = false; UpdatePreview(); };
        strike.Unchecked += (_, _) => UpdatePreview();
        strike2.Checked += (_, _) => { if (strike.IsChecked == true)  strike.IsChecked  = false; UpdatePreview(); };
        strike2.Unchecked += (_, _) => UpdatePreview();
        superS.Checked += (_, _) => { if (subS.IsChecked == true) subS.IsChecked = false; UpdatePreview(); };
        superS.Unchecked += (_, _) => UpdatePreview();
        subS.Checked   += (_, _) => { if (superS.IsChecked == true) superS.IsChecked = false; UpdatePreview(); };
        subS.Unchecked   += (_, _) => UpdatePreview();
        small.Checked  += (_, _) => { if (allc.IsChecked == true) allc.IsChecked = false; UpdatePreview(); };
        small.Unchecked  += (_, _) => UpdatePreview();
        allc.Checked   += (_, _) => { if (small.IsChecked == true) small.IsChecked = false; UpdatePreview(); };
        allc.Unchecked   += (_, _) => UpdatePreview();

        pick.Click += (_, _) =>
        {
            using var dlg = new System.Windows.Forms.ColorDialog { AllowFullOpen = true, FullOpen = true };
            var current = TryParseColor(colorHex.Text);
            dlg.Color = System.Drawing.Color.FromArgb(current.A, current.R, current.G, current.B);
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                colorHex.Text = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
                UpdatePreview();
            }
        };
        bgPick.Click += (_, _) =>
        {
            using var dlg = new System.Windows.Forms.ColorDialog { AllowFullOpen = true, FullOpen = true };
            if (!string.IsNullOrWhiteSpace(bgHex.Text))
            {
                var current = TryParseColor(bgHex.Text);
                dlg.Color = System.Drawing.Color.FromArgb(current.A, current.R, current.G, current.B);
            }
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                bgHex.Text = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
                UpdatePreview();
            }
        };
        bgNone.Click += (_, _) => { bgHex.Text = ""; UpdatePreview(); };

        Result? r = null;
        ok.Click += (_, _) =>
        {
            var alignSel = (alignBox.SelectedItem as string) switch
            {
                "Center" => PDFEditor.Models.TextAlign.Center,
                "Right" => PDFEditor.Models.TextAlign.Right,
                "Justify" => PDFEditor.Models.TextAlign.Justify,
                _ => PDFEditor.Models.TextAlign.Left
            };
            var bgText = (bgHex.Text ?? "").Trim();
            r = new Result(
                text.Text,
                (font.SelectedItem as string) ?? font.Text ?? "Arial",
                double.TryParse(size.Text, out var s) ? s : (size.SelectedItem is string ss && double.TryParse(ss, out var s2) ? s2 : defaultSize),
                SelectedFontWeight(),
                italic.IsChecked == true,
                underline.IsChecked == true,
                colorHex.Text,
                alignSel,
                BackgroundHex: string.IsNullOrEmpty(bgText) ? null : bgText,
                Strikethrough: strike.IsChecked == true,
                DoubleStrikethrough: strike2.IsChecked == true,
                Superscript: superS.IsChecked == true,
                Subscript: subS.IsChecked == true,
                SmallCaps: small.IsChecked == true,
                AllCaps: allc.IsChecked == true);
            w.DialogResult = true;
        };
        text.Loaded += (_, _) => { text.Focus(); text.SelectAll(); };
        UpdatePreview();
        return w.ShowDialog() == true ? r : null;
    }

    private static IEnumerable<string> GetAllFontFamilyNames()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fam in Fonts.SystemFontFamilies)
        {
            var name = fam.Source;
            if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
        }
        return names;
    }

    private static Brush TryParseBrush(string hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(c);
        }
        catch { return Brushes.Black; }
    }

    private static Color TryParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.Black; }
    }

    private static T WithRow<T>(T e, int r) where T : UIElement { Grid.SetRow(e, r); return e; }
    private static T WithCol<T>(T e, int c) where T : UIElement { Grid.SetColumn(e, c); return e; }
}
