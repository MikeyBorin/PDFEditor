using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PDFEditor.Services;

namespace PDFEditor.Controls;

public static class WatermarkDialog
{
    public record Result(string Text, double FontSize, string ColorHex, double Opacity, double Angle);

    public static Result? Show()
    {
        var w = new Window { Title = "Add Watermark", Width = 400, Owner = Application.Current?.MainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, SizeToContent = SizeToContent.Height };
        var g = new Grid { Margin = new Thickness(16) };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        void Add(int r, string label, UIElement ctrl)
        {
            var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
            Grid.SetRow(tb, r); Grid.SetColumn(tb, 0); g.Children.Add(tb);
            Grid.SetRow(ctrl, r); Grid.SetColumn(ctrl, 1);
            if (ctrl is FrameworkElement fe) fe.Margin = new Thickness(0, 4, 0, 4);
            g.Children.Add(ctrl);
        }

        var txt = new TextBox { Text = "DRAFT" };
        var size = new TextBox { Text = "72" };
        var color = new TextBox { Text = "#FF0000" };
        var opacity = new TextBox { Text = "0.30" };
        var angle = new TextBox { Text = "-30" };
        Add(0, "Text:", txt);
        Add(1, "Font size (pt):", size);
        Add(2, "Color (hex):", color);
        Add(3, "Opacity (0-1):", opacity);
        Add(4, "Angle (°):", angle);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "Apply", Width = 80, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, Height = 28, IsCancel = true };
        btns.Children.Add(ok); btns.Children.Add(cancel);
        Grid.SetRow(btns, 5); Grid.SetColumn(btns, 0); Grid.SetColumnSpan(btns, 2);
        g.Children.Add(btns);

        w.Content = g;
        Result? r = null;
        ok.Click += (_, _) =>
        {
            r = new Result(txt.Text,
                double.TryParse(size.Text, out var sz) ? sz : 72,
                color.Text,
                double.TryParse(opacity.Text, out var op) ? op : 0.3,
                double.TryParse(angle.Text, out var an) ? an : -30);
            w.DialogResult = true;
        };
        return w.ShowDialog() == true ? r : null;
    }
}

public static class HeadersFootersDialog
{
    public static ContentOverlayService.HeaderFooterOptions? Show()
    {
        var w = new Window { Title = "Headers & Footers", Width = 640, Owner = Application.Current?.MainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, SizeToContent = SizeToContent.Height };
        var g = new Grid { Margin = new Thickness(16) };
        for (int i = 0; i < 5; i++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < 3; i++) g.ColumnDefinitions.Add(new ColumnDefinition());

        var hint = new TextBlock { Text = "Use placeholders: {page}, {total}, {date}, {filename}", Margin = new Thickness(0, 0, 0, 8), FontStyle = FontStyles.Italic };
        Grid.SetColumnSpan(hint, 3); g.Children.Add(hint);
        Grid.SetRow(hint, 0);

        TextBox NewBox(string placeholder) { var t = new TextBox { Margin = new Thickness(4), Tag = placeholder }; return t; }
        TextBlock Lbl(string s) => new TextBlock { Text = s, Margin = new Thickness(4, 4, 4, 2), FontWeight = FontWeights.SemiBold };
        void AddCell(int r, int c, UIElement e) { Grid.SetRow(e, r); Grid.SetColumn(e, c); g.Children.Add(e); }

        AddCell(1, 0, Lbl("Header Left")); AddCell(1, 1, Lbl("Header Center")); AddCell(1, 2, Lbl("Header Right"));
        var hL = NewBox("HL"); var hC = NewBox("HC"); var hR = NewBox("HR");
        AddCell(2, 0, hL); AddCell(2, 1, hC); AddCell(2, 2, hR);

        AddCell(3, 0, Lbl("Footer Left")); AddCell(3, 1, Lbl("Footer Center")); AddCell(3, 2, Lbl("Footer Right"));
        var fL = NewBox("FL"); var fC = NewBox("FC"); var fR = NewBox("FR");
        fC.Text = "Page {page} of {total}";
        AddCell(4, 0, fL); AddCell(4, 1, fC); AddCell(4, 2, fR);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "Apply", Width = 80, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, Height = 28, IsCancel = true };
        btns.Children.Add(ok); btns.Children.Add(cancel);

        var outer = new DockPanel();
        DockPanel.SetDock(btns, Dock.Bottom);
        outer.Children.Add(btns);
        outer.Children.Add(g);
        w.Content = outer;

        ContentOverlayService.HeaderFooterOptions? result = null;
        ok.Click += (_, _) =>
        {
            result = new ContentOverlayService.HeaderFooterOptions
            {
                HeaderLeft = hL.Text, HeaderCenter = hC.Text, HeaderRight = hR.Text,
                FooterLeft = fL.Text, FooterCenter = fC.Text, FooterRight = fR.Text
            };
            w.DialogResult = true;
        };
        return w.ShowDialog() == true ? result : null;
    }
}

public static class BatesDialog
{
    public record Result(string Prefix, int StartNumber, int Digits, bool BottomRight);

    public static Result? Show()
    {
        var w = new Window { Title = "Bates Numbering", Width = 360, Owner = Application.Current?.MainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, SizeToContent = SizeToContent.Height };
        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(new TextBlock { Text = "Prefix (e.g. ACME):", Margin = new Thickness(0, 0, 0, 4) });
        var prefix = new TextBox { Text = "" };
        sp.Children.Add(prefix);
        sp.Children.Add(new TextBlock { Text = "Start number:", Margin = new Thickness(0, 8, 0, 4) });
        var start = new TextBox { Text = "1" };
        sp.Children.Add(start);
        sp.Children.Add(new TextBlock { Text = "Digits (padding):", Margin = new Thickness(0, 8, 0, 4) });
        var digits = new TextBox { Text = "6" };
        sp.Children.Add(digits);
        var cbRight = new CheckBox { Content = "Bottom-right (uncheck for bottom-left)", IsChecked = true, Margin = new Thickness(0, 10, 0, 0) };
        sp.Children.Add(cbRight);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = "Apply", Width = 80, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, Height = 28, IsCancel = true };
        btns.Children.Add(ok); btns.Children.Add(cancel);
        sp.Children.Add(btns);

        w.Content = sp;
        Result? r = null;
        ok.Click += (_, _) =>
        {
            r = new Result(prefix.Text,
                int.TryParse(start.Text, out var s) ? s : 1,
                int.TryParse(digits.Text, out var d) ? d : 6,
                cbRight.IsChecked == true);
            w.DialogResult = true;
        };
        return w.ShowDialog() == true ? r : null;
    }
}

public static class CropDialog
{
    public record Result(double LeftPt, double RightPt, double TopPt, double BottomPt);
    public static Result? Show()
    {
        var w = new Window { Title = "Crop All Pages", Width = 360, Owner = Application.Current?.MainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, SizeToContent = SizeToContent.Height };
        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(new TextBlock { Text = "Trim margins in points (1 pt = 1/72 inch):", Margin = new Thickness(0, 0, 0, 8) });
        var grid = new Grid();
        for (int i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        void Add(int r, string label, TextBox tb) { var l = new TextBlock { Text = label, Margin = new Thickness(0, 4, 8, 4) }; Grid.SetRow(l, r); grid.Children.Add(l); Grid.SetRow(tb, r); Grid.SetColumn(tb, 1); tb.Margin = new Thickness(0, 4, 0, 4); grid.Children.Add(tb); }
        var left = new TextBox { Text = "36" }; var right = new TextBox { Text = "36" };
        var top = new TextBox { Text = "36" }; var bottom = new TextBox { Text = "36" };
        Add(0, "Left (pt):", left); Add(1, "Right (pt):", right); Add(2, "Top (pt):", top); Add(3, "Bottom (pt):", bottom);
        sp.Children.Add(grid);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = "Apply", Width = 80, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, Height = 28, IsCancel = true };
        btns.Children.Add(ok); btns.Children.Add(cancel);
        sp.Children.Add(btns);
        w.Content = sp;
        Result? r = null;
        ok.Click += (_, _) =>
        {
            r = new Result(double.TryParse(left.Text, out var l) ? l : 0,
                            double.TryParse(right.Text, out var ri) ? ri : 0,
                            double.TryParse(top.Text, out var t) ? t : 0,
                            double.TryParse(bottom.Text, out var b) ? b : 0);
            w.DialogResult = true;
        };
        return w.ShowDialog() == true ? r : null;
    }
}

public static class InsertImageDialogHelpers
{
    public static InsertImageDialog.Result? PlaceOnly(string imagePath, double defaultX = 0.10, double defaultY = 0.10, double defaultW = 0.30, double defaultH = 0.10)
        => InsertImageDialog.ShowInternal(imagePath, defaultX, defaultY, defaultW, defaultH);
}

public static class InsertImageDialog
{
    public record Result(string ImagePath, double XNorm, double YNorm, double WidthNorm, double HeightNorm);

    public static Result? Show()
    {
        var dlg = new OpenFileDialog { Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*" };
        if (dlg.ShowDialog() != true) return null;
        return ShowInternal(dlg.FileName);
    }

    internal static Result? ShowInternal(string imagePath, double defaultX = 0.10, double defaultY = 0.10, double defaultW = 0.30, double defaultH = 0.30)
    {
        var w = new Window { Title = "Place Image", Width = 400, Owner = Application.Current?.MainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, SizeToContent = SizeToContent.Height };
        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(new TextBlock { Text = $"File: {System.IO.Path.GetFileName(imagePath)}", Margin = new Thickness(0, 0, 0, 8) });
        sp.Children.Add(new TextBlock { Text = "Position and size as percentage of page (0-1):" });
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        for (int i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        void Add(int r, string label, TextBox tb) { var lbl = new TextBlock { Text = label, Margin = new Thickness(0, 4, 8, 4) }; Grid.SetRow(lbl, r); grid.Children.Add(lbl); Grid.SetRow(tb, r); Grid.SetColumn(tb, 1); tb.Margin = new Thickness(0, 4, 0, 4); grid.Children.Add(tb); }
        var x = new TextBox { Text = defaultX.ToString("0.00") }; var y = new TextBox { Text = defaultY.ToString("0.00") };
        var ww = new TextBox { Text = defaultW.ToString("0.00") }; var hh = new TextBox { Text = defaultH.ToString("0.00") };
        Add(0, "X:", x); Add(1, "Y:", y); Add(2, "Width:", ww); Add(3, "Height:", hh);
        sp.Children.Add(grid);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = "Insert", Width = 80, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, Height = 28, IsCancel = true };
        btns.Children.Add(ok); btns.Children.Add(cancel);
        sp.Children.Add(btns);
        w.Content = sp;
        Result? r = null;
        ok.Click += (_, _) =>
        {
            r = new Result(imagePath,
                double.TryParse(x.Text, out var xv) ? xv : defaultX,
                double.TryParse(y.Text, out var yv) ? yv : defaultY,
                double.TryParse(ww.Text, out var wv) ? wv : defaultW,
                double.TryParse(hh.Text, out var hv) ? hv : defaultH);
            w.DialogResult = true;
        };
        return w.ShowDialog() == true ? r : null;
    }
}
