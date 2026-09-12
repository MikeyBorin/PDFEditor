using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PDFEditor.Models;
using PDFEditor.ViewModels;

namespace PDFEditor.Controls;

/// <summary>
/// A Canvas that overlays a rendered page image, renders annotations for that page,
/// and creates new annotations based on the currently-selected tool.
/// </summary>
public class AnnotationLayer : Canvas
{
    public static readonly DependencyProperty PageProperty =
        DependencyProperty.Register(nameof(Page), typeof(PageViewModel), typeof(AnnotationLayer),
            new PropertyMetadata(null, OnPageChanged));

    public static readonly DependencyProperty MainVMProperty =
        DependencyProperty.Register(nameof(MainVM), typeof(MainViewModel), typeof(AnnotationLayer),
            new PropertyMetadata(null, OnMainVMChanged));

    private static void OnMainVMChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (AnnotationLayer)d;
        if (e.OldValue is MainViewModel old) old.TransientHighlightChanged -= self.Rebuild;
        if (e.NewValue is MainViewModel nw) nw.TransientHighlightChanged += self.Rebuild;
        self.Rebuild();
    }

    public PageViewModel? Page
    {
        get => (PageViewModel?)GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    public MainViewModel? MainVM
    {
        get => (MainViewModel?)GetValue(MainVMProperty);
        set => SetValue(MainVMProperty, value);
    }

    private PdfAnnotation? _drafting;
    private UIElement? _draftingVisual;
    private Point _dragStart;

    // Drag-move state for existing annotations (Select tool).
    private PdfAnnotation? _movingAnnotation;
    private Point _moveOffsetInPage;

    // Resize state.
    private PdfAnnotation? _resizingAnnotation;
    private string? _resizingHandle;   // "E", "S", or "SE"
    private double _resizeAnchorRight, _resizeAnchorBottom;

    // Pan (hand tool) state. Panning scrolls the ancestor ScrollViewer
    // instead of touching the page, so it works at any zoom, leaves the
    // document untouched and pushes nothing onto either undo stack.
    private ScrollViewer? _panScroller;
    private Point _panStart;               // grab point, in scroller coords
    private double _panStartH, _panStartV; // scroll offsets at grab time

    // Sticky checkbox size — shared across all AnnotationLayer instances so
    // moving to a different page keeps the template dimensions. Set by the
    // first real checkbox drag; used to auto-size subsequent clicks so a run
    // of "click, click, click" produces uniform boxes.
    private static double _lastCheckboxWidth;
    private static double _lastCheckboxHeight;

    public AnnotationLayer()
    {
        Background = Brushes.Transparent;
        Cursor = Cursors.Arrow;
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        MouseEnter += (_, _) => UpdateCursor();
        MouseMove += TrackMousePosition;
        MouseRightButtonUp += OnMouseRightButtonUp;
    }

    // --- Paste context menu (right-click on empty page area) -----------------

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Page is null || MainVM is null) return;
        var pos = e.GetPosition(this);
        var w = Page.PixelWidth > 0 ? Page.PixelWidth : ActualWidth;
        var h = Page.PixelHeight > 0 ? Page.PixelHeight : ActualHeight;
        if (w <= 0 || h <= 0) return;
        var nx = System.Math.Clamp(pos.X / w, 0, 1);
        var ny = System.Math.Clamp(pos.Y / h, 0, 1);

        var menu = new ContextMenu
        {
            PlacementTarget = this,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
            StaysOpen = false
        };
        ContextMenu = menu; // parent to a visual so the popup gets its screen origin

        bool hasText = false;
        bool hasImage = false;
        try { hasText = System.Windows.Clipboard.ContainsText(); } catch { }
        try { hasImage = System.Windows.Clipboard.ContainsImage(); } catch { }

        var pasteText = new MenuItem { Header = "Paste _text", IsEnabled = hasText };
        pasteText.Click += (_, _) => TryPasteTextAt(nx, ny);
        menu.Items.Add(pasteText);

        var pasteImage = new MenuItem { Header = "Paste _image", IsEnabled = hasImage };
        pasteImage.Click += (_, _) => TryPasteImageAt(nx, ny);
        menu.Items.Add(pasteImage);

        if (!hasText && !hasImage)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "(Nothing to paste)", IsEnabled = false });
        }

        menu.IsOpen = true;
        e.Handled = true;
    }

    private void TryPasteTextAt(double nx, double ny)
    {
        if (Page is null || MainVM is null) return;
        string text;
        try { text = System.Windows.Clipboard.GetText(); }
        catch { return; }
        if (string.IsNullOrEmpty(text)) return;

        var wrapW = System.Math.Min(0.6, System.Math.Max(0.1, 0.9 - nx));
        var stamp = new PdfAnnotation
        {
            PageIndex = Page.PageIndex,
            Kind = AnnotationKind.TextStamp,
            X = nx, Y = ny, Width = wrapW, Height = 0.05,
            Color = MainVM.CurrentColor,
            Text = text,
            FontFamily = MainVM.CurrentFontFamily,
            FontSize = MainVM.CurrentFontSize,
            FontWeight = MainVM.CurrentFontWeight,
            Italic = MainVM.CurrentItalic,
            Underline = MainVM.CurrentUnderline,
            Align = MainVM.CurrentAlign,
        };
        AddAnnotationWithUndo(stamp);
        MainVM.SelectedAnnotation = stamp;
        MainVM.CurrentTool = ToolMode.Select;
        MainVM.StatusText = $"Pasted text ({text.Length} chars). Save to flatten.";
    }

    private void TryPasteImageAt(double nx, double ny)
    {
        if (Page is null || MainVM is null) return;
        System.Windows.Media.Imaging.BitmapSource? bmp;
        try { bmp = System.Windows.Clipboard.GetImage(); }
        catch { return; }
        if (bmp is null) return;

        string tempPath;
        try
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ArtiMaxPDFEditor");
            System.IO.Directory.CreateDirectory(dir);
            tempPath = System.IO.Path.Combine(dir, $"paste-{System.Guid.NewGuid():N}.png");
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var fs = System.IO.File.Create(tempPath);
            encoder.Save(fs);
        }
        catch (System.Exception ex)
        {
            MainVM.StatusText = "Paste image failed: " + ex.Message;
            return;
        }

        // Size the image so its aspect matches the source and its width is ~30%
        // of the page. Height derived from the page's own aspect ratio.
        double aspect = bmp.PixelHeight > 0 ? (double)bmp.PixelWidth / bmp.PixelHeight : 1.0;
        double normW = 0.30;
        double normH = normW * ((double)Page.PixelWidth / Page.PixelHeight) / aspect;
        if (normH <= 0.01 || normH > 0.6) normH = 0.15;

        double normX = System.Math.Clamp(nx - normW / 2, 0, 1 - normW);
        double normY = System.Math.Clamp(ny - normH / 2, 0, 1 - normH);

        var img = new PdfAnnotation
        {
            PageIndex = Page.PageIndex,
            Kind = AnnotationKind.Image,
            X = normX, Y = normY, Width = normW, Height = normH,
            ImagePath = tempPath
        };
        AddAnnotationWithUndo(img);
        MainVM.SelectedAnnotation = img;
        MainVM.CurrentTool = ToolMode.Select;
        MainVM.StatusText = "Pasted image. Drag with Select tool; Save to flatten.";
    }

    private bool TryEditUnderCursor(Point posLocal)
    {
        if (Page is null || MainVM is null) return false;
        var w = Page.PixelWidth > 0 ? Page.PixelWidth : ActualWidth;
        var h = Page.PixelHeight > 0 ? Page.PixelHeight : ActualHeight;
        var nx = posLocal.X / w; var ny = posLocal.Y / h;
        for (int i = Page.Annotations.Count - 1; i >= 0; i--)
        {
            var a = Page.Annotations[i];
            if (!HitTest(a, nx, ny)) continue;

            if (a.Kind == AnnotationKind.TextStamp)
            {
                var hex = "#" + a.Color.R.ToString("X2") + a.Color.G.ToString("X2") + a.Color.B.ToString("X2");
                var r = TextStampDialog.Show(
                    defaultText: a.Text ?? "",
                    defaultFont: string.IsNullOrEmpty(a.FontFamily) ? "Arial" : a.FontFamily,
                    defaultSize: a.FontSize > 0 ? a.FontSize : 14,
                    defaultFontWeight: a.FontWeight > 0 ? a.FontWeight : 400,
                    defaultItalic: a.Italic,
                    defaultUnderline: a.Underline,
                    defaultColorHex: hex,
                    defaultAlign: a.Align,
                    defaultBackgroundHex: BgHexOrNull(a.BackgroundColor),
                    defaultStrikethrough: a.Strikethrough,
                    defaultDoubleStrikethrough: a.DoubleStrikethrough,
                    defaultSuperscript: a.Superscript,
                    defaultSubscript: a.Subscript,
                    defaultSmallCaps: a.SmallCaps,
                    defaultAllCaps: a.AllCaps);
                if (r != null)
                {
                    try
                    {
                        var c = (Color)ColorConverter.ConvertFromString(r.ColorHex);
                        a.Text = r.Text;
                        a.FontFamily = r.FontFamily;
                        a.FontSize = r.FontSize;
                        a.FontWeight = r.FontWeight;
                        a.Italic = r.Italic;
                        a.Underline = r.Underline;
                        a.Align = r.Align;
                        a.Color = c;
                        a.BackgroundColor = ParseHexOrNull(r.BackgroundHex);
                        a.Strikethrough = r.Strikethrough;
                        a.DoubleStrikethrough = r.DoubleStrikethrough;
                        a.Superscript = r.Superscript;
                        a.Subscript = r.Subscript;
                        a.SmallCaps = r.SmallCaps;
                        a.AllCaps = r.AllCaps;
                        MainVM.RememberFontChoice(r);
                        // Do NOT touch X/Y or Width (preserves resize) — keep layout exactly.
                        Page.RaiseAnnotationChanged();
                        MainVM.StatusText = "Text updated.";
                    }
                    catch { }
                }
                MainVM.SelectedAnnotation = a;
                return true;
            }
            if (a.Kind == AnnotationKind.StickyNote || a.Kind == AnnotationKind.Callout)
            {
                var hex = "#" + a.Color.R.ToString("X2") + a.Color.G.ToString("X2") + a.Color.B.ToString("X2");
                var r = TextStampDialog.Show(
                    defaultText: a.Text ?? "",
                    defaultFont: string.IsNullOrEmpty(a.FontFamily) ? "Arial" : a.FontFamily,
                    defaultSize: a.FontSize > 0 ? a.FontSize : 12,
                    defaultFontWeight: a.FontWeight > 0 ? a.FontWeight : 400, defaultItalic: a.Italic, defaultUnderline: a.Underline,
                    defaultColorHex: hex,
                    defaultAlign: a.Align,
                    defaultBackgroundHex: BgHexOrNull(a.BackgroundColor) ?? "#FFEB82",
                    defaultStrikethrough: a.Strikethrough,
                    defaultDoubleStrikethrough: a.DoubleStrikethrough,
                    defaultSuperscript: a.Superscript,
                    defaultSubscript: a.Subscript,
                    defaultSmallCaps: a.SmallCaps,
                    defaultAllCaps: a.AllCaps);
                if (r != null)
                {
                    try
                    {
                        var c = (Color)ColorConverter.ConvertFromString(r.ColorHex);
                        a.Text = r.Text; a.FontFamily = r.FontFamily; a.FontSize = r.FontSize;
                        a.FontWeight = r.FontWeight; a.Italic = r.Italic; a.Underline = r.Underline;
                        a.Align = r.Align; a.Color = c;
                        a.BackgroundColor = ParseHexOrNull(r.BackgroundHex);
                        a.Strikethrough = r.Strikethrough;
                        a.DoubleStrikethrough = r.DoubleStrikethrough;
                        a.Superscript = r.Superscript;
                        a.Subscript = r.Subscript;
                        a.SmallCaps = r.SmallCaps;
                        a.AllCaps = r.AllCaps;
                        MainVM.RememberFontChoice(r);
                        Page.RaiseAnnotationChanged();
                        MainVM.StatusText = a.Kind == AnnotationKind.StickyNote ? "Note updated." : "Callout updated.";
                    }
                    catch { }
                }
                MainVM.SelectedAnnotation = a;
                return true;
            }
        }
        return false;
    }

    private void TrackMousePosition(object sender, MouseEventArgs e)
    {
        if (Page is null || MainVM is null) return;
        var w = Page.PixelWidth > 0 ? Page.PixelWidth : ActualWidth;
        var h = Page.PixelHeight > 0 ? Page.PixelHeight : ActualHeight;
        if (w <= 0 || h <= 0) return;
        var p = e.GetPosition(this);
        MainVM.LastHover = (Page.PageIndex, System.Math.Clamp(p.X / w, 0, 1), System.Math.Clamp(p.Y / h, 0, 1));
    }

    /// <summary>Walks up the visual tree to the ScrollViewer that scrolls the
    /// page list. Pan deltas are measured in its coordinates rather than the
    /// layer's, so the Viewbox zoom scale does not distort the drag.</summary>
    private ScrollViewer? FindScroller()
    {
        DependencyObject? d = this;
        while (d != null)
        {
            if (d is ScrollViewer sv) return sv;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    private void UpdateCursor()
    {
        if (MainVM is null) { Cursor = Cursors.Arrow; return; }
        Cursor = MainVM.CurrentTool switch
        {
            ToolMode.Select => Cursors.Arrow,
            ToolMode.Pan => Cursors.Hand,
            ToolMode.Ink => Cursors.Pen,
            ToolMode.Erase => Cursors.No,
            _ => Cursors.Cross
        };
    }

    private static void OnPageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (AnnotationLayer)d;
        if (e.OldValue is PageViewModel old)
        {
            old.Annotations.CollectionChanged -= self.OnAnnotationsChanged;
            old.AnnotationChanged -= self.Rebuild;
        }
        if (e.NewValue is PageViewModel np)
        {
            np.Annotations.CollectionChanged += self.OnAnnotationsChanged;
            np.AnnotationChanged += self.Rebuild;
            self.Rebuild();
        }
        else
        {
            self.Children.Clear();
        }
        // Subscribe to VM's transient highlight event once MainVM is known.
        if (self.MainVM != null)
        {
            self.MainVM.TransientHighlightChanged -= self.Rebuild;
            self.MainVM.TransientHighlightChanged += self.Rebuild;
        }
    }

    private void OnAnnotationsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        Children.Clear();
        if (Page is null) return;
        // If layout hasn't measured us yet, defer until it does.
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            Dispatcher.BeginInvoke(new System.Action(Rebuild), System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }
        foreach (var a in Page.Annotations)
        {
            try
            {
                var v = BuildVisual(a);
                if (v != null) Children.Add(v);
            }
            catch { /* skip one bad annotation, keep rendering the rest */ }
        }
        // Transient search-hit highlight (yellow pulse-like box). Cleared on next page click.
        var th = MainVM?.TransientHighlight;
        if (th.HasValue && th.Value.PageIndex == Page.PageIndex)
        {
            var pw = Page.PixelWidth > 0 ? Page.PixelWidth : ActualWidth;
            var ph = Page.PixelHeight > 0 ? Page.PixelHeight : ActualHeight;
            var pad = 4.0;
            var hi = new Rectangle
            {
                Width = th.Value.W * pw + pad * 2,
                Height = th.Value.H * ph + pad * 2,
                Fill = new SolidColorBrush(Color.FromArgb(120, 255, 220, 0)),
                Stroke = new SolidColorBrush(Color.FromArgb(255, 255, 140, 0)),
                StrokeThickness = 2,
                IsHitTestVisible = false
            };
            SetLeft(hi, th.Value.X * pw - pad); SetTop(hi, th.Value.Y * ph - pad);
            Children.Add(hi);
        }

        // Selection outline — snapped to the annotation's visible bounds — plus resize handles.
        var sel = MainVM?.SelectedAnnotation;
        if (sel != null && Page.Annotations.Contains(sel))
        {
            var pw = Page.PixelWidth > 0 ? Page.PixelWidth : ActualWidth;
            var ph = Page.PixelHeight > 0 ? Page.PixelHeight : ActualHeight;
            var (bx, by, bw, bh) = VisualBounds(sel, Page);
            var pad = 3.0;
            var boxLeft = bx * pw - pad;
            var boxTop = by * ph - pad;
            var boxW = bw * pw + pad * 2;
            var boxH = bh * ph + pad * 2;
            var outline = new Rectangle
            {
                Width = boxW,
                Height = boxH,
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };
            SetLeft(outline, boxLeft); SetTop(outline, boxTop);
            Children.Add(outline);

            // Resize handles: E for width, S for height, SE for both.
            // Text stamps derive their height from wrapped content, so S is meaningless for them.
            AddResizeHandle(sel, "E",  boxLeft + boxW - 5, boxTop + boxH / 2 - 5, Cursors.SizeWE);
            if (sel.Kind != AnnotationKind.TextStamp)
                AddResizeHandle(sel, "S",  boxLeft + boxW / 2 - 5, boxTop + boxH - 5, Cursors.SizeNS);
            AddResizeHandle(sel, "SE", boxLeft + boxW - 5, boxTop + boxH - 5, Cursors.SizeNWSE);

            // Callouts get an extra handle at the arrow tip so it can be moved
            // independently of the text box.
            if (sel.Kind == AnnotationKind.Callout)
            {
                AddResizeHandle(sel, "Anchor", sel.AnchorX * pw - 5, sel.AnchorY * ph - 5, Cursors.Hand);
            }
        }
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
        if (MainVM != null)
            MainVM.StatusText = $"Layer {ActualWidth:F0}x{ActualHeight:F0} — {Page.Annotations.Count} annotation(s) on page {Page.PageIndex + 1}.";
    }

    private UIElement? BuildVisual(PdfAnnotation a)
    {
        var w = Page?.PixelWidth ?? ActualWidth;
        var h = Page?.PixelHeight ?? ActualHeight;
        if (w <= 0) w = ActualWidth;
        if (h <= 0) h = ActualHeight;
        if (w <= 0 || h <= 0) return null;
        var brush = new SolidColorBrush(a.Color);

        // The Canvas coordinate space is IMAGE-PIXELS (Border.Width = PixelWidth),
        // but a.FontSize is semantically POINTS (that's what the flatten path in
        // AnnotationService uses via XFont, and it's what the user sees in the
        // TextStampDialog). Multiply by px-per-point when handing FontSize to a
        // WPF TextBlock so the on-screen preview matches the flattened output and
        // matches source PDF text at the same point size.
        var pxPerPt = (MainVM?.RenderDpi ?? 150) / 72.0;

        switch (a.Kind)
        {
            case AnnotationKind.Highlight:
                var hl = new Rectangle
                {
                    Width = a.Width * w,
                    Height = a.Height * h,
                    Fill = new SolidColorBrush(Color.FromArgb(96, a.Color.R, a.Color.G, a.Color.B)),
                    IsHitTestVisible = false // canvas handles all clicks; drag is by bounds check
                };
                SetLeft(hl, a.X * w); SetTop(hl, a.Y * h);
                return hl;

            case AnnotationKind.Whiteout:
                var wo = new Rectangle
                {
                    Width = a.Width * w,
                    Height = a.Height * h,
                    Fill = Brushes.White,
                    Stroke = Brushes.LightGray,
                    StrokeDashArray = new DoubleCollection { 4, 3 },
                    StrokeThickness = 1,
                    IsHitTestVisible = false // canvas handles all clicks; drag is by bounds check
                };
                SetLeft(wo, a.X * w); SetTop(wo, a.Y * h);
                return wo;

            case AnnotationKind.Image:
                if (!string.IsNullOrEmpty(a.ImagePath) && System.IO.File.Exists(a.ImagePath))
                {
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.UriSource = new System.Uri(a.ImagePath);
                    img.EndInit();
                    img.Freeze();
                    var image = new System.Windows.Controls.Image
                    {
                        Source = img,
                        Stretch = Stretch.Fill,
                        Width = a.Width * w,
                        Height = a.Height * h,
                        IsHitTestVisible = false
                    };
                    SetLeft(image, a.X * w); SetTop(image, a.Y * h);
                    return image;
                }
                return null;

            case AnnotationKind.Redaction:
                var rd = new Rectangle
                {
                    Width = a.Width * w,
                    Height = a.Height * h,
                    Fill = Brushes.Black,
                    IsHitTestVisible = false // canvas handles all clicks; drag is by bounds check
                };
                SetLeft(rd, a.X * w); SetTop(rd, a.Y * h);
                return rd;

            case AnnotationKind.Rectangle:
                var r = new Rectangle
                {
                    Width = a.Width * w,
                    Height = a.Height * h,
                    Stroke = a.Filled ? null : brush,
                    StrokeThickness = a.Filled ? 0 : a.StrokeThickness,
                    Fill = a.Filled ? brush : null,
                    IsHitTestVisible = false // canvas handles all clicks; drag is by bounds check
                };
                SetLeft(r, a.X * w); SetTop(r, a.Y * h);
                return r;

            case AnnotationKind.Ellipse:
                var el = new Ellipse
                {
                    Width = a.Width * w,
                    Height = a.Height * h,
                    Stroke = a.Filled ? null : brush,
                    StrokeThickness = a.Filled ? 0 : a.StrokeThickness,
                    Fill = a.Filled ? brush : null,
                    IsHitTestVisible = false // canvas handles all clicks; drag is by bounds check
                };
                SetLeft(el, a.X * w); SetTop(el, a.Y * h);
                return el;

            case AnnotationKind.CheckboxField:
                // Draft/placeholder for a form checkbox to be written into the
                // AcroForm at save time. Rendered as a dashed blue rectangle
                // with a subtle fill so it's obviously a form field and not a
                // regular Rectangle annotation.
                var cbFill = System.Windows.Media.Color.FromArgb(24, 0x1F, 0x6F, 0xEB);
                var cbBorder = System.Windows.Media.Color.FromRgb(0x1F, 0x6F, 0xEB);
                var cb = new Rectangle
                {
                    Width = a.Width * w,
                    Height = a.Height * h,
                    Stroke = new SolidColorBrush(cbBorder),
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 3, 2 },
                    Fill = new SolidColorBrush(cbFill),
                    IsHitTestVisible = false
                };
                SetLeft(cb, a.X * w); SetTop(cb, a.Y * h);
                return cb;

            case AnnotationKind.Ink:
                if (a.InkPoints.Count < 2) return null;
                var poly = new Polyline
                {
                    Stroke = brush,
                    StrokeThickness = a.StrokeThickness,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = false // canvas handles all clicks; drag is by bounds check
                };
                foreach (var p in a.InkPoints) poly.Points.Add(new Point(p.X * w, p.Y * h));
                return poly;

            case AnnotationKind.StickyNote:
                // Visible post-it style: yellow rounded background + wrapped text.
                var noteText = new TextBlock
                {
                    Text = a.Text ?? "",
                    TextWrapping = TextWrapping.Wrap,
                    Padding = new Thickness(6),
                    FontSize = (a.FontSize > 0 ? a.FontSize : 12) * pxPerPt,
                    FontFamily = new FontFamily(string.IsNullOrEmpty(a.FontFamily) ? "Arial" : a.FontFamily),
                    FontWeight = FontWeight.FromOpenTypeWeight(a.FontWeight > 0 ? a.FontWeight : 400),
                    FontStyle = a.Italic ? FontStyles.Italic : FontStyles.Normal,
                    TextDecorations = a.Underline ? TextDecorations.Underline : null,
                    TextAlignment = a.Align switch
                    {
                        TextAlign.Center => TextAlignment.Center,
                        TextAlign.Right => TextAlignment.Right,
                        TextAlign.Justify => TextAlignment.Justify,
                        _ => TextAlignment.Left
                    },
                    Foreground = new SolidColorBrush(a.Color == Colors.Black || a.Color == default ? Colors.Black : a.Color),
                    MaxWidth = 240
                };
                var noteBg = a.BackgroundColor is Color bc
                    ? Color.FromArgb(230, bc.R, bc.G, bc.B)
                    : Color.FromArgb(230, 255, 235, 130);   // default yellow post-it
                var noteBorder = new System.Windows.Controls.Border
                {
                    Background = new SolidColorBrush(noteBg),
                    BorderBrush = Brushes.DarkGoldenrod,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Child = noteText,
                    Tag = a
                };
                SetLeft(noteBorder, a.X * w); SetTop(noteBorder, a.Y * h);
                return noteBorder;

            case AnnotationKind.Callout:
                {
                    var boxLeft = a.X * w;
                    var boxTop  = a.Y * h;
                    var boxW    = System.Math.Max(20, a.Width  * w);
                    var boxH    = System.Math.Max(16, a.Height * h);
                    var anchorX = a.AnchorX * w;
                    var anchorY = a.AnchorY * h;

                    var container = new Canvas
                    {
                        Width = w, Height = h,
                        IsHitTestVisible = false
                    };

                    // Line from the box edge closest to the anchor, out to the anchor point.
                    var (lx, ly) = ClosestEdgePoint(boxLeft, boxTop, boxW, boxH, anchorX, anchorY);
                    var stroke = new SolidColorBrush(a.Color);
                    container.Children.Add(new Line
                    {
                        X1 = lx, Y1 = ly,
                        X2 = anchorX, Y2 = anchorY,
                        Stroke = stroke,
                        StrokeThickness = System.Math.Max(1, a.StrokeThickness)
                    });

                    // Arrowhead at the anchor tip.
                    var arrow = MakeArrowhead(lx, ly, anchorX, anchorY, a.Color);
                    if (arrow != null) container.Children.Add(arrow);

                    // The text box.
                    var coFs = (a.FontSize > 0 ? a.FontSize : 12) * pxPerPt;
                    var calloutText = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Padding = new Thickness(6),
                        FontSize = a.Superscript || a.Subscript ? System.Math.Max(6, coFs * 0.7) : coFs,
                        FontFamily = new FontFamily(string.IsNullOrEmpty(a.FontFamily) ? "Arial" : a.FontFamily),
                        FontWeight = FontWeight.FromOpenTypeWeight(a.FontWeight > 0 ? a.FontWeight : 400),
                        FontStyle = a.Italic ? FontStyles.Italic : FontStyles.Normal,
                        TextDecorations = BuildDecorations(a),
                        TextAlignment = a.Align switch
                        {
                            TextAlign.Center => TextAlignment.Center,
                            TextAlign.Right => TextAlignment.Right,
                            TextAlign.Justify => TextAlignment.Justify,
                            _ => TextAlignment.Left
                        },
                        Foreground = new SolidColorBrush(a.Color == default ? Colors.Black : a.Color)
                    };
                    PopulateInlinesForCaps(calloutText, a.Text ?? "", a);
                    var calloutBg = a.BackgroundColor is Color cbc
                        ? Color.FromArgb(230, cbc.R, cbc.G, cbc.B)
                        : Color.FromArgb(230, 255, 235, 130);
                    var box = new System.Windows.Controls.Border
                    {
                        Background = new SolidColorBrush(calloutBg),
                        BorderBrush = stroke,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(3),
                        Width = boxW, Height = boxH,
                        Child = calloutText
                    };
                    Canvas.SetLeft(box, boxLeft);
                    Canvas.SetTop(box,  boxTop);
                    container.Children.Add(box);

                    SetLeft(container, 0); SetTop(container, 0);
                    return container;
                }

            case AnnotationKind.TextStamp:
                {
                    // Width from the annotation constrains wrap; if unset, use a sensible default.
                    var maxTextW = (a.Width > 0.001 ? a.Width : 0.4) * w;
                    // Clamp font size: WPF TextBlock throws on FontSize <= 0 or NaN/Infinity.
                    var pointSize = a.FontSize > 0 && !double.IsNaN(a.FontSize) && !double.IsInfinity(a.FontSize)
                        ? a.FontSize
                        : System.Math.Max(10, (a.Height * h) / pxPerPt);
                    if (pointSize <= 0 || double.IsNaN(pointSize) || double.IsInfinity(pointSize)) pointSize = 14;
                    var fs = pointSize * pxPerPt;
                    var tb = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Width = maxTextW,
                        MaxWidth = maxTextW,
                        Foreground = brush,
                        FontSize = a.Superscript || a.Subscript ? System.Math.Max(6, fs * 0.7) : fs,
                        FontFamily = new FontFamily(string.IsNullOrEmpty(a.FontFamily) ? "Arial" : a.FontFamily),
                        FontWeight = FontWeight.FromOpenTypeWeight(a.FontWeight > 0 ? a.FontWeight : 400),
                        FontStyle = a.Italic ? FontStyles.Italic : FontStyles.Normal,
                        TextDecorations = BuildDecorations(a),
                        TextAlignment = a.Align switch
                        {
                            TextAlign.Center => TextAlignment.Center,
                            TextAlign.Right => TextAlignment.Right,
                            TextAlign.Justify => TextAlignment.Justify,
                            _ => TextAlignment.Left
                        },
                        Tag = a
                    };
                    PopulateInlinesForCaps(tb, a.Text ?? "", a);
                    if (a.Superscript) tb.Padding = new Thickness(0, 0, 0, fs * 0.35);
                    else if (a.Subscript) tb.Padding = new Thickness(0, fs * 0.35, 0, 0);
                    if (a.BackgroundColor is Color tsbg)
                    {
                        // Wrap in a padded Border so the background is visible around
                        // the glyphs. Slight rounding to match Note / Callout styling.
                        var wrap = new System.Windows.Controls.Border
                        {
                            Background = new SolidColorBrush(tsbg),
                            CornerRadius = new CornerRadius(2),
                            Padding = new Thickness(2),
                            Child = tb,
                            Tag = a
                        };
                        SetLeft(wrap, a.X * w); SetTop(wrap, a.Y * h);
                        return wrap;
                    }
                    SetLeft(tb, a.X * w); SetTop(tb, a.Y * h);
                    return tb;
                }
        }
        return null;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Page is null || MainVM is null) return;

        // Any click clears the transient search-hit highlight.
        if (MainVM.TransientHighlight != null) MainVM.TransientHighlight = null;

        // Hand tool: grab the view and scroll it. Handled before every other
        // branch so a pan drag never selects, edits or draws anything.
        if (MainVM.CurrentTool == ToolMode.Pan)
        {
            _panScroller = FindScroller();
            if (_panScroller != null)
            {
                _panStart  = e.GetPosition(_panScroller);
                _panStartH = _panScroller.HorizontalOffset;
                _panStartV = _panScroller.VerticalOffset;
                Cursor = Cursors.ScrollAll;
                CaptureMouse();
            }
            e.Handled = true;
            return;
        }

        // Double-click on an existing text/note annotation opens its editor,
        // regardless of current tool. Skip the create/select paths.
        if (e.ClickCount >= 2)
        {
            if (TryEditUnderCursor(e.GetPosition(this))) { e.Handled = true; return; }
            // Nothing under the cursor: with the Select tool, treat this as a
            // shortcut to drop a text stamp here — engage TextStamp and fall
            // through to the tool-handling branches below, which will open the
            // dialog and auto-revert to Select afterward.
            if (MainVM.CurrentTool == ToolMode.Select)
            {
                MainVM.CurrentTool = ToolMode.TextStamp;
            }
        }

        var tool = MainVM.CurrentTool;
        var pos = e.GetPosition(this);
        var w = Page.PixelWidth > 0 ? Page.PixelWidth : ActualWidth;
        var h = Page.PixelHeight > 0 ? Page.PixelHeight : ActualHeight;
        var nx = pos.X / w; var ny = pos.Y / h;

        if (tool == ToolMode.Select)
        {
            // Drag existing annotation. Pick top-most hit (last in list).
            for (int i = Page.Annotations.Count - 1; i >= 0; i--)
            {
                var a = Page.Annotations[i];
                if (HitTest(a, nx, ny))
                {
                    _movingAnnotation = a;
                    _moveOffsetInPage = new Point(nx - a.X, ny - a.Y);
                    MainVM.SelectedAnnotation = a;
                    Cursor = Cursors.SizeAll;
                    CaptureMouse();
                    Rebuild(); // repaint with selection border
                    return;
                }
            }
            // Empty-space click. Deselect, then start a drag-region draft
            // (same shape as SelectText). If the user releases without moving
            // the mouse, OnMouseUp's minSize check discards the draft — plain
            // click still just deselects. A real drag hands off to the source-
            // text extract flow (same handler as the Sel Text button).
            MainVM.SelectedAnnotation = null;
            _dragStart = pos;
            _drafting = new PdfAnnotation
            {
                PageIndex = Page.PageIndex,
                Kind = AnnotationKind.Rectangle,   // draft preview only
                X = nx, Y = ny, Width = 0, Height = 0,
                Color = System.Windows.Media.Colors.DodgerBlue,
                StrokeThickness = MainVM.CurrentThickness
            };
            CaptureMouse();
            Rebuild();
            return;
        }

        _dragStart = pos;

        if (tool == ToolMode.StickyNote)
        {
            // Use the text-stamp dialog so notes get font family / size / bold /
            // italic / underline / alignment / colour just like text stamps.
            var hex = "#" + MainVM.CurrentColor.R.ToString("X2") + MainVM.CurrentColor.G.ToString("X2") + MainVM.CurrentColor.B.ToString("X2");
            var r = TextStampDialog.Show(
                defaultFont: MainVM.CurrentFontFamily,
                defaultSize: MainVM.CurrentFontSize,
                defaultFontWeight: MainVM.CurrentFontWeight,
                defaultItalic: MainVM.CurrentItalic,
                defaultUnderline: MainVM.CurrentUnderline,
                defaultColorHex: hex,
                defaultAlign: MainVM.CurrentAlign,
                defaultBackgroundHex: "#FFEB82");
            if (r != null && !string.IsNullOrWhiteSpace(r.Text))
            {
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(r.ColorHex);
                    var note = new PdfAnnotation
                    {
                        PageIndex = Page.PageIndex, Kind = AnnotationKind.StickyNote,
                        X = nx, Y = ny, Width = 0.02, Height = 0.02,
                        Color = c, Text = r.Text,
                        FontFamily = r.FontFamily, FontSize = r.FontSize,
                        FontWeight = r.FontWeight, Italic = r.Italic, Underline = r.Underline,
                        Align = r.Align,
                        BackgroundColor = ParseHexOrNull(r.BackgroundHex),
                        Strikethrough = r.Strikethrough,
                        DoubleStrikethrough = r.DoubleStrikethrough,
                        Superscript = r.Superscript, Subscript = r.Subscript,
                        SmallCaps = r.SmallCaps, AllCaps = r.AllCaps
                    };
                    AddAnnotationWithUndo(note);
                    MainVM.RememberFontChoice(r);
                    MainVM.SelectedAnnotation = note;
                    MainVM.CurrentTool = ToolMode.Select;
                }
                catch { }
            }
            return;
        }

        if (tool == ToolMode.TextStamp)
        {
            var hex = "#" + MainVM.CurrentColor.R.ToString("X2") + MainVM.CurrentColor.G.ToString("X2") + MainVM.CurrentColor.B.ToString("X2");
            var r = TextStampDialog.Show(
                defaultFont: MainVM.CurrentFontFamily,
                defaultSize: MainVM.CurrentFontSize,
                defaultFontWeight: MainVM.CurrentFontWeight,
                defaultItalic: MainVM.CurrentItalic,
                defaultUnderline: MainVM.CurrentUnderline,
                defaultColorHex: hex,
                defaultAlign: MainVM.CurrentAlign,
                defaultBackgroundHex: null);
            if (r != null && !string.IsNullOrWhiteSpace(r.Text))
            {
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(r.ColorHex);
                    // Wrap width: aim for the remaining page width to the right of the click,
                    // capped at 60% of page. User can drag the resize handles to change it.
                    var wrapW = System.Math.Min(0.6, System.Math.Max(0.1, 0.9 - nx));
                    var stamp = new PdfAnnotation
                    {
                        PageIndex = Page.PageIndex, Kind = AnnotationKind.TextStamp,
                        X = nx, Y = ny, Width = wrapW, Height = 0.05,
                        Color = c, Text = r.Text,
                        FontFamily = r.FontFamily, FontSize = r.FontSize,
                        FontWeight = r.FontWeight, Italic = r.Italic, Underline = r.Underline,
                        Align = r.Align,
                        BackgroundColor = ParseHexOrNull(r.BackgroundHex),
                        Strikethrough = r.Strikethrough,
                        DoubleStrikethrough = r.DoubleStrikethrough,
                        Superscript = r.Superscript, Subscript = r.Subscript,
                        SmallCaps = r.SmallCaps, AllCaps = r.AllCaps
                    };
                    AddAnnotationWithUndo(stamp);
                    MainVM.RememberFontChoice(r);
                    // One-shot tool: revert to Select so the next click drags/edits the stamp
                    // instead of dropping another one.
                    MainVM.SelectedAnnotation = stamp;
                    MainVM.CurrentTool = ToolMode.Select;
                }
                catch { }
            }
            return;
        }

        if (tool == ToolMode.Erase)
        {
            EraseAt(pos);
            return;
        }

        if (tool == ToolMode.Callout)
        {
            // Click = the point being called out (arrow tip). Rich text dialog so
            // callouts get font / size / colour / style like text stamps.
            var hex = "#" + MainVM.CurrentColor.R.ToString("X2") + MainVM.CurrentColor.G.ToString("X2") + MainVM.CurrentColor.B.ToString("X2");
            var r = TextStampDialog.Show(
                defaultFont: MainVM.CurrentFontFamily,
                defaultSize: MainVM.CurrentFontSize,
                defaultFontWeight: MainVM.CurrentFontWeight,
                defaultItalic: MainVM.CurrentItalic,
                defaultUnderline: MainVM.CurrentUnderline,
                defaultColorHex: hex,
                defaultAlign: MainVM.CurrentAlign,
                defaultBackgroundHex: "#FFEB82");
            if (r != null && !string.IsNullOrWhiteSpace(r.Text))
            {
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(r.ColorHex);
                    var boxOffsetX = 0.10;
                    var boxOffsetY = 0.06;
                    var boxW = 0.22;
                    var boxH = 0.06;
                    var boxX = System.Math.Clamp(nx + boxOffsetX, 0, 1 - boxW);
                    var boxY = System.Math.Clamp(ny + boxOffsetY, 0, 1 - boxH);
                    var callout = new PdfAnnotation
                    {
                        PageIndex = Page.PageIndex, Kind = AnnotationKind.Callout,
                        X = boxX, Y = boxY, Width = boxW, Height = boxH,
                        AnchorX = nx, AnchorY = ny,
                        Color = c,
                        StrokeThickness = 1.5,
                        Text = r.Text,
                        FontFamily = r.FontFamily, FontSize = r.FontSize,
                        FontWeight = r.FontWeight, Italic = r.Italic, Underline = r.Underline,
                        Align = r.Align,
                        BackgroundColor = ParseHexOrNull(r.BackgroundHex),
                        Strikethrough = r.Strikethrough,
                        DoubleStrikethrough = r.DoubleStrikethrough,
                        Superscript = r.Superscript, Subscript = r.Subscript,
                        SmallCaps = r.SmallCaps, AllCaps = r.AllCaps
                    };
                    AddAnnotationWithUndo(callout);
                    MainVM.RememberFontChoice(r);
                    MainVM.SelectedAnnotation = callout;
                    MainVM.CurrentTool = ToolMode.Select;
                }
                catch { }
            }
            return;
        }

        if (tool == ToolMode.Tick || tool == ToolMode.Cross || tool == ToolMode.Bullet)
        {
            // Form-mark drop: single glyph in bold at a fixed default size.
            // Deliberately does NOT read/write CurrentFontSize — a form-fill mark
            // shouldn't hijack the user's text-stamp font retention.
            var glyph = tool switch
            {
                ToolMode.Tick   => "✓",
                ToolMode.Cross  => "✗",
                ToolMode.Bullet => "•",
                _ => "?"
            };
            var mark = new PdfAnnotation
            {
                PageIndex = Page.PageIndex, Kind = AnnotationKind.TextStamp,
                X = nx, Y = ny, Width = 0.03, Height = 0.03,
                Color = MainVM.CurrentColor, Text = glyph,
                FontFamily = "Segoe UI Symbol", FontSize = 18, FontWeight = 700
            };
            AddAnnotationWithUndo(mark);
            // Tool stays armed — clicking again drops another mark. Use the Select
            // tool if you want to drag/resize/edit an existing one.
            return;
        }

        _drafting = new PdfAnnotation
        {
            PageIndex = Page.PageIndex,
            Kind = tool switch
            {
                ToolMode.Highlight => AnnotationKind.Highlight,
                ToolMode.Rectangle or ToolMode.RectangleFilled => AnnotationKind.Rectangle,
                ToolMode.Ellipse   or ToolMode.EllipseFilled   => AnnotationKind.Ellipse,
                ToolMode.Ink => AnnotationKind.Ink,
                ToolMode.Whiteout => AnnotationKind.Whiteout,
                ToolMode.InsertCheckbox => AnnotationKind.CheckboxField,
                ToolMode.SelectText or ToolMode.SelectImage => AnnotationKind.Rectangle, // draft preview only
                _ => AnnotationKind.Rectangle
            },
            X = nx, Y = ny, Width = 0, Height = 0,
            Color = tool is ToolMode.SelectText or ToolMode.SelectImage
                ? System.Windows.Media.Colors.DodgerBlue
                : tool is ToolMode.InsertCheckbox
                    ? System.Windows.Media.Color.FromRgb(0x1F, 0x6F, 0xEB)  // form-field blue
                    : MainVM.CurrentColor,
            StrokeThickness = MainVM.CurrentThickness,
            Filled = tool is ToolMode.RectangleFilled or ToolMode.EllipseFilled
        };

        if (_drafting.Kind == AnnotationKind.Ink)
            _drafting.InkPoints.Add((nx, ny));

        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (Page is null) return;

        // Pan in progress: translate the drag into scroll offsets. Measured in
        // the scroller's own coordinates so the Viewbox zoom scale does not
        // distort the movement.
        if (_panScroller != null)
        {
            var pp = e.GetPosition(_panScroller);
            _panScroller.ScrollToHorizontalOffset(_panStartH - (pp.X - _panStart.X));
            _panScroller.ScrollToVerticalOffset(_panStartV - (pp.Y - _panStart.Y));
            return;
        }

        if (_resizingAnnotation != null && _resizingHandle != null)
        {
            var pw = Page.PixelWidth > 0 ? Page.PixelWidth : ActualWidth;
            var ph = Page.PixelHeight > 0 ? Page.PixelHeight : ActualHeight;
            var mp = e.GetPosition(this);
            var nx = System.Math.Clamp(mp.X / pw, 0, 1);
            var ny = System.Math.Clamp(mp.Y / ph, 0, 1);
            // Checkbox fields stay square through resize. Snap whichever handle
            // the user is dragging so the box grows or shrinks along both axes
            // by the same visual pixel amount.
            if (_resizingAnnotation.Kind == AnnotationKind.CheckboxField)
            {
                double sidePx;
                switch (_resizingHandle)
                {
                    case "E":  sidePx = mp.X - _resizingAnnotation.X * pw; break;
                    case "S":  sidePx = mp.Y - _resizingAnnotation.Y * ph; break;
                    default:   sidePx = System.Math.Max(mp.X - _resizingAnnotation.X * pw,
                                                        mp.Y - _resizingAnnotation.Y * ph); break;
                }
                sidePx = System.Math.Max(6, sidePx);
                _resizingAnnotation.Width  = sidePx / pw;
                _resizingAnnotation.Height = sidePx / ph;
                Rebuild();
                return;
            }
            switch (_resizingHandle)
            {
                case "E":
                    _resizingAnnotation.Width = System.Math.Max(0.02, nx - _resizingAnnotation.X);
                    break;
                case "S":
                    _resizingAnnotation.Height = System.Math.Max(0.01, ny - _resizingAnnotation.Y);
                    break;
                case "SE":
                    _resizingAnnotation.Width = System.Math.Max(0.02, nx - _resizingAnnotation.X);
                    _resizingAnnotation.Height = System.Math.Max(0.01, ny - _resizingAnnotation.Y);
                    break;
                case "Anchor":
                    _resizingAnnotation.AnchorX = nx;
                    _resizingAnnotation.AnchorY = ny;
                    break;
            }
            Rebuild();
            return;
        }

        if (_movingAnnotation != null)
        {
            var pw = Page.PixelWidth > 0 ? Page.PixelWidth : ActualWidth;
            var ph = Page.PixelHeight > 0 ? Page.PixelHeight : ActualHeight;
            var mp = e.GetPosition(this);
            var newX = System.Math.Clamp(mp.X / pw - _moveOffsetInPage.X, 0, 1 - System.Math.Max(0.005, _movingAnnotation.Width));
            var newY = System.Math.Clamp(mp.Y / ph - _moveOffsetInPage.Y, 0, 1 - System.Math.Max(0.005, _movingAnnotation.Height));
            // Callouts: drag the box and the arrow anchor together, preserving their offset.
            if (_movingAnnotation.Kind == AnnotationKind.Callout)
            {
                _movingAnnotation.AnchorX += (newX - _movingAnnotation.X);
                _movingAnnotation.AnchorY += (newY - _movingAnnotation.Y);
            }
            _movingAnnotation.X = newX;
            _movingAnnotation.Y = newY;
            Rebuild();
            return;
        }

        if (_drafting is null) return;
        var pos = e.GetPosition(this);
        var w = Page.PixelWidth > 0 ? Page.PixelWidth : ActualWidth;
        var h = Page.PixelHeight > 0 ? Page.PixelHeight : ActualHeight;

        if (_drafting.Kind == AnnotationKind.Ink)
        {
            _drafting.InkPoints.Add((pos.X / w, pos.Y / h));
        }
        else if (_drafting.Kind == AnnotationKind.CheckboxField)
        {
            // Constrain to a visual square. Take the larger of |dx|,|dy| as
            // the side length in screen pixels, then convert to normalized
            // dims. Width/Height differ in the normalized space because they
            // divide by page width vs. height (page isn't square), so the
            // rendered widget ends up square in point / pixel space.
            var dx = pos.X - _dragStart.X;
            var dy = pos.Y - _dragStart.Y;
            var side = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy));
            var sx = dx >= 0 ? _dragStart.X : _dragStart.X - side;
            var sy = dy >= 0 ? _dragStart.Y : _dragStart.Y - side;
            _drafting.X = sx / w;
            _drafting.Y = sy / h;
            _drafting.Width  = side / w;
            _drafting.Height = side / h;
        }
        else
        {
            var x = System.Math.Min(_dragStart.X, pos.X) / w;
            var y = System.Math.Min(_dragStart.Y, pos.Y) / h;
            _drafting.X = x; _drafting.Y = y;
            _drafting.Width = System.Math.Abs(pos.X - _dragStart.X) / w;
            _drafting.Height = System.Math.Abs(pos.Y - _dragStart.Y) / h;
        }

        // Live-preview: remove previous draft visual then re-add.
        if (_draftingVisual != null) Children.Remove(_draftingVisual);
        _draftingVisual = BuildVisual(_drafting);
        if (_draftingVisual != null) Children.Add(_draftingVisual);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        if (_panScroller != null)
        {
            _panScroller = null;
            UpdateCursor();
            return;
        }
        if (_resizingAnnotation != null)
        {
            _resizingAnnotation = null;
            _resizingHandle = null;
            UpdateCursor();
            return;
        }
        if (_movingAnnotation != null)
        {
            _movingAnnotation = null;
            UpdateCursor();
            return;
        }
        if (_drafting is null) return;

        // Discard tiny drags (accidental clicks).
        var minSize = 0.005;
        var ok = _drafting.Kind == AnnotationKind.Ink
            ? _drafting.InkPoints.Count > 2
            : _drafting.Width > minSize && _drafting.Height > minSize;

        // Sticky checkbox size: a click (or micro-drag) drops a new checkbox
        // at the same size as the last one the user placed via a real drag.
        // A real drag both places at that size AND updates the sticky template.
        if (_drafting.Kind == AnnotationKind.CheckboxField)
        {
            if (ok)
            {
                _lastCheckboxWidth  = _drafting.Width;
                _lastCheckboxHeight = _drafting.Height;
            }
            else if (_lastCheckboxWidth > 0 && _lastCheckboxHeight > 0)
            {
                var pw = Page!.PixelWidth  > 0 ? Page.PixelWidth  : ActualWidth;
                var ph = Page.PixelHeight > 0 ? Page.PixelHeight : ActualHeight;
                _drafting.Width  = _lastCheckboxWidth;
                _drafting.Height = _lastCheckboxHeight;
                // Anchor top-left at the click point (dragStart), clamped in-page.
                _drafting.X = System.Math.Clamp(_dragStart.X / pw, 0, 1 - _lastCheckboxWidth);
                _drafting.Y = System.Math.Clamp(_dragStart.Y / ph, 0, 1 - _lastCheckboxHeight);
                ok = true;
            }
        }

        if (_draftingVisual != null) Children.Remove(_draftingVisual);
        _draftingVisual = null;

        // Special tools consume the draft rectangle instead of committing it as an annotation.
        // Select-tool drag falls in here too: a real drag runs the SelectText action
        // (extract source text, offer copy / replace); a plain click has ok=false
        // and just clears the draft, leaving the empty-space deselect from OnMouseDown.
        var currentTool = MainVM?.CurrentTool;
        if (currentTool == ToolMode.SelectText
            || currentTool == ToolMode.SelectImage
            || currentTool == ToolMode.Select)
        {
            if (ok)
            {
                var region = (_drafting.X, _drafting.Y, _drafting.Width, _drafting.Height);
                var pageIdx = Page!.PageIndex;
                // Select-tool drag reuses the SelectText action.
                var kind = currentTool.Value == ToolMode.Select ? ToolMode.SelectText : currentTool.Value;
                _drafting = null;
                Rebuild();
                Dispatcher.BeginInvoke(new System.Action(() => InvokeRegionAction(pageIdx, region, kind)),
                    System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }
            // Plain click with Select tool — attempt to toggle an AcroForm
            // checkbox whose /Rect contains the click. If nothing hits, this
            // is a no-op (no rebuild) and the click just clears any lingering
            // draft, matching prior behaviour.
            if (currentTool == ToolMode.Select && MainVM != null && Page != null)
            {
                var pw = Page.PixelWidth  > 0 ? Page.PixelWidth  : ActualWidth;
                var ph = Page.PixelHeight > 0 ? Page.PixelHeight : ActualHeight;
                if (pw > 0 && ph > 0)
                {
                    var clickNx = System.Math.Clamp(_dragStart.X / pw, 0, 1);
                    var clickNy = System.Math.Clamp(_dragStart.Y / ph, 0, 1);
                    var pageIdxLocal = Page.PageIndex;
                    var vm = MainVM;
                    // Fire-and-forget: TryToggleFormCheckboxAt does a byte-level
                    // rebuild ONLY on hit, so a miss costs a cheap dict walk.
                    _ = vm.TryToggleFormCheckboxAt(pageIdxLocal, clickNx, clickNy);
                }
            }
            _drafting = null;
            Rebuild();
            return;
        }

        if (ok) AddAnnotationWithUndo(_drafting);
        else Rebuild();

        _drafting = null;
    }

    private void InvokeRegionAction(int pageIndex, (double X, double Y, double W, double H) region, ToolMode tool)
    {
        if (MainVM is null) return;
        MainVM.HandleRegionSelection(pageIndex, region.X, region.Y, region.W, region.H, tool);
    }

    private static string? BgHexOrNull(Color? c)
        => c is null ? null : $"#{c.Value.R:X2}{c.Value.G:X2}{c.Value.B:X2}";

    /// <summary>Apply AllCaps to a text string for WPF rendering. SmallCaps
    /// uses per-character Runs instead (see PopulateInlinesForCaps).</summary>
    private static string TransformCaps(string text, PdfAnnotation a)
        => a.AllCaps ? (text ?? "").ToUpper() : (text ?? "");

    /// <summary>Set a TextBlock's content according to the annotation's caps flags.
    /// AllCaps → single uppercased Text. SmallCaps → Inlines/Runs where each
    /// originally-lowercase character is uppercased at ~78% of the base font size,
    /// giving the proper "SMALL CAPS" typographic look. Neither → plain text.</summary>
    private static void PopulateInlinesForCaps(TextBlock tb, string raw, PdfAnnotation a)
    {
        raw ??= "";
        if (a.AllCaps)
        {
            tb.Text = raw.ToUpper();
            return;
        }
        if (!a.SmallCaps)
        {
            tb.Text = raw;
            return;
        }
        // Proper small-caps: group consecutive chars by "was originally lowercase"
        // and render each run at either full size or 78%. Uppercase everything so
        // the small-caps glyphs actually look like small capitals rather than
        // scaled-down lowercase letters.
        tb.Text = null;
        tb.Inlines.Clear();
        var full = tb.FontSize;
        var smallSize = System.Math.Max(6, full * 0.78);
        int i = 0;
        while (i < raw.Length)
        {
            bool isLower = char.IsLower(raw[i]);
            int j = i + 1;
            while (j < raw.Length && char.IsLower(raw[j]) == isLower) j++;
            var runText = raw.Substring(i, j - i).ToUpper();
            tb.Inlines.Add(new System.Windows.Documents.Run(runText)
            {
                FontSize = isLower ? smallSize : full
            });
            i = j;
        }
    }

    /// <summary>Combine Underline + Strikethrough(s) into a WPF decoration collection.
    /// DoubleStrikethrough renders as two parallel Strikethrough decorations offset
    /// above and below the default strike position, matching how the flattened PDF
    /// draws double strikes.</summary>
    private static TextDecorationCollection BuildDecorations(PdfAnnotation a)
    {
        var d = new TextDecorationCollection();
        if (a.Underline) d.Add(TextDecorations.Underline);
        if (a.DoubleStrikethrough)
        {
            d.Add(new System.Windows.TextDecoration
            {
                Location = System.Windows.TextDecorationLocation.Strikethrough,
                PenOffset = 1.5,
                PenOffsetUnit = System.Windows.TextDecorationUnit.Pixel
            });
            d.Add(new System.Windows.TextDecoration
            {
                Location = System.Windows.TextDecorationLocation.Strikethrough,
                PenOffset = -1.5,
                PenOffsetUnit = System.Windows.TextDecorationUnit.Pixel
            });
        }
        else if (a.Strikethrough)
        {
            d.Add(TextDecorations.Strikethrough);
        }
        return d;
    }

    private static Color? ParseHexOrNull(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return null; }
    }

    /// <summary>Add an annotation to the current page and register an undo that
    /// removes it. Use this instead of Page.Annotations.Add(...) so Ctrl+Z reverses
    /// new annotations (Whiteout, Rectangle, Ellipse, Ink, Highlight, Text stamps,
    /// Sticky notes, Callouts, Marks — everything).</summary>
    private void AddAnnotationWithUndo(PdfAnnotation a)
    {
        if (Page is null) return;
        var page = Page;
        var vm = MainVM;
        page.Annotations.Add(a);
        vm?.PushAnnotationUndo(() =>
        {
            page.Annotations.Remove(a);
            if (vm.SelectedAnnotation == a) vm.SelectedAnnotation = null;
        });
    }

    private void AddResizeHandle(PdfAnnotation a, string handle, double left, double top, Cursor cursor)
    {
        var h = new Rectangle
        {
            Width = 10, Height = 10,
            Fill = Brushes.DodgerBlue,
            Stroke = Brushes.White,
            StrokeThickness = 1,
            Cursor = cursor,
            IsHitTestVisible = true,
            Tag = handle
        };
        SetLeft(h, left); SetTop(h, top);
        h.MouseLeftButtonDown += (s, e) =>
        {
            if (Page is null) return;
            _resizingAnnotation = a;
            _resizingHandle = handle;
            _resizeAnchorRight = a.X + a.Width;
            _resizeAnchorBottom = a.Y + a.Height;
            CaptureMouse();
            e.Handled = true;
        };
        Children.Add(h);
    }

    /// <summary>Approximate visible bounds of an annotation in normalized page coords.</summary>
    private static (double x, double y, double w, double h) VisualBounds(PdfAnnotation a, PageViewModel? page)
    {
        double pageW = page?.PixelWidth ?? 1000;
        double pageH = page?.PixelHeight ?? 1000;
        double w = a.Width, h = a.Height;
        switch (a.Kind)
        {
            case AnnotationKind.StickyNote:
                // Post-it is rendered ~ 240px wide with wrapped text height (default ~ 24px).
                w = 240.0 / pageW; h = System.Math.Max(24.0 / pageH, 0.02);
                break;
            case AnnotationKind.TextStamp:
                {
                    var fs = a.FontSize > 0 ? a.FontSize : 14;
                    var maxW = a.Width > 0.001 ? a.Width : 0.4;
                    var charsPerLine = System.Math.Max(1, maxW * pageW / (fs * 0.55));
                    int totalLines = 0;
                    foreach (var line in (a.Text ?? "").Split('\n'))
                        totalLines += System.Math.Max(1, (int)System.Math.Ceiling(line.Length / charsPerLine));
                    w = maxW;
                    h = System.Math.Max(0.02, totalLines * fs * 1.2 / pageH);
                }
                break;
            case AnnotationKind.Ink:
                if (a.InkPoints.Count > 0)
                {
                    double minX = 1, minY = 1, maxX = 0, maxY = 0;
                    foreach (var p in a.InkPoints)
                    {
                        if (p.X < minX) minX = p.X;
                        if (p.Y < minY) minY = p.Y;
                        if (p.X > maxX) maxX = p.X;
                        if (p.Y > maxY) maxY = p.Y;
                    }
                    return (minX, minY, System.Math.Max(0.01, maxX - minX), System.Math.Max(0.01, maxY - minY));
                }
                break;
        }
        if (w < 0.01) w = 0.03;
        if (h < 0.01) h = 0.03;
        return (a.X, a.Y, w, h);
    }

    private bool HitTest(PdfAnnotation a, double nx, double ny)
    {
        // Ink: check distance to each segment.
        if (a.Kind == AnnotationKind.Ink && a.InkPoints.Count >= 2)
        {
            var tol = 0.01;
            for (int i = 1; i < a.InkPoints.Count; i++)
            {
                var (x1, y1) = a.InkPoints[i - 1];
                var (x2, y2) = a.InkPoints[i];
                if (DistanceToSegment(nx, ny, x1, y1, x2, y2) < tol) return true;
            }
            return false;
        }
        var (bx, by, bw, bh) = VisualBounds(a, Page);
        return nx >= bx && nx <= bx + bw && ny >= by && ny <= by + bh;
    }

    /// <summary>Given a box and an external point, return the box-edge midpoint closest to the point.
    /// Used for the callout's leader line so it emerges from the box side facing the anchor.</summary>
    private static (double x, double y) ClosestEdgePoint(double boxLeft, double boxTop, double boxW, double boxH, double px, double py)
    {
        var cx = boxLeft + boxW / 2;
        var cy = boxTop  + boxH / 2;
        var dx = px - cx;
        var dy = py - cy;
        // Compare weighted absolute components to pick the face.
        if (System.Math.Abs(dx) * boxH > System.Math.Abs(dy) * boxW)
        {
            var x = dx > 0 ? boxLeft + boxW : boxLeft;
            return (x, cy);
        }
        else
        {
            var y = dy > 0 ? boxTop + boxH : boxTop;
            return (cx, y);
        }
    }

    /// <summary>Small filled triangle at the anchor tip, base pointing back along the leader line.</summary>
    private static System.Windows.Shapes.Polygon? MakeArrowhead(double fromX, double fromY, double toX, double toY, Color color)
    {
        var dx = toX - fromX;
        var dy = toY - fromY;
        var len = System.Math.Sqrt(dx * dx + dy * dy);
        if (len < 4) return null;
        var ux = dx / len;
        var uy = dy / len;
        const double size = 10.0;
        var baseX = toX - ux * size;
        var baseY = toY - uy * size;
        var px = -uy;
        var py = ux;
        var half = size / 2;
        var poly = new System.Windows.Shapes.Polygon { Fill = new SolidColorBrush(color), IsHitTestVisible = false };
        poly.Points.Add(new Point(baseX + px * half, baseY + py * half));
        poly.Points.Add(new Point(baseX - px * half, baseY - py * half));
        poly.Points.Add(new Point(toX, toY));
        return poly;
    }

    private static double DistanceToSegment(double px, double py, double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1; var dy = y2 - y1;
        var len2 = dx * dx + dy * dy;
        if (len2 < 1e-9) return System.Math.Sqrt((px - x1) * (px - x1) + (py - y1) * (py - y1));
        var t = ((px - x1) * dx + (py - y1) * dy) / len2;
        t = System.Math.Clamp(t, 0, 1);
        var cx = x1 + t * dx; var cy = y1 + t * dy;
        return System.Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
    }

    private void EraseAt(Point pos)
    {
        if (Page is null) return;
        var w = Page.PixelWidth > 0 ? Page.PixelWidth : ActualWidth;
        var h = Page.PixelHeight > 0 ? Page.PixelHeight : ActualHeight;
        var nx = pos.X / w; var ny = pos.Y / h;
        for (int i = Page.Annotations.Count - 1; i >= 0; i--)
        {
            if (HitTest(Page.Annotations[i], nx, ny))
            {
                if (MainVM != null && ReferenceEquals(MainVM.SelectedAnnotation, Page.Annotations[i]))
                    MainVM.SelectedAnnotation = null;
                Page.Annotations.RemoveAt(i);
                return;
            }
        }
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var r = base.ArrangeOverride(arrangeSize);
        // Ensure each child gets measured/arranged inside the layer bounds — without this,
        // children added between measure passes render at zero size.
        foreach (UIElement child in Children)
        {
            child.Measure(arrangeSize);
            var w = child.DesiredSize.Width;
            var h = child.DesiredSize.Height;
            var x = GetLeft(child); if (double.IsNaN(x)) x = 0;
            var y = GetTop(child);  if (double.IsNaN(y)) y = 0;
            child.Arrange(new Rect(x, y, w, h));
        }
        return r;
    }
}
