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

    public AnnotationLayer()
    {
        Background = Brushes.Transparent;
        Cursor = Cursors.Arrow;
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        MouseEnter += (_, _) => UpdateCursor();
        MouseMove += TrackMousePosition;
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
                    defaultBold: a.Bold,
                    defaultItalic: a.Italic,
                    defaultUnderline: a.Underline,
                    defaultColorHex: hex,
                    defaultAlign: a.Align);
                if (r != null)
                {
                    try
                    {
                        var c = (Color)ColorConverter.ConvertFromString(r.ColorHex);
                        a.Text = r.Text;
                        a.FontFamily = r.FontFamily;
                        a.FontSize = r.FontSize;
                        a.Bold = r.Bold;
                        a.Italic = r.Italic;
                        a.Underline = r.Underline;
                        a.Align = r.Align;
                        a.Color = c;
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
            if (a.Kind == AnnotationKind.StickyNote)
            {
                var text = PromptDialog.Ask("Edit Note", "Note text:", a.Text ?? "");
                if (text != null)
                {
                    a.Text = text;
                    Page.RaiseAnnotationChanged();
                    MainVM.StatusText = "Note updated.";
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

    private void UpdateCursor()
    {
        if (MainVM is null) { Cursor = Cursors.Arrow; return; }
        Cursor = MainVM.CurrentTool switch
        {
            ToolMode.Select => Cursors.Arrow,
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
                    FontSize = a.FontSize > 0 ? a.FontSize : 12,
                    FontFamily = new FontFamily(string.IsNullOrEmpty(a.FontFamily) ? "Arial" : a.FontFamily),
                    Foreground = Brushes.Black,
                    MaxWidth = 240
                };
                var noteBorder = new System.Windows.Controls.Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(230, 255, 235, 130)),
                    BorderBrush = Brushes.DarkGoldenrod,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Child = noteText,
                    Tag = a
                };
                SetLeft(noteBorder, a.X * w); SetTop(noteBorder, a.Y * h);
                return noteBorder;

            case AnnotationKind.TextStamp:
                {
                    // Width from the annotation constrains wrap; if unset, use a sensible default.
                    var maxTextW = (a.Width > 0.001 ? a.Width : 0.4) * w;
                    // Clamp font size: WPF TextBlock throws on FontSize <= 0 or NaN/Infinity.
                    var fs = a.FontSize > 0 && !double.IsNaN(a.FontSize) && !double.IsInfinity(a.FontSize)
                        ? a.FontSize
                        : System.Math.Max(10, a.Height * h);
                    if (fs <= 0 || double.IsNaN(fs) || double.IsInfinity(fs)) fs = 14;
                    var tb = new TextBlock
                    {
                        Text = a.Text ?? "",
                        TextWrapping = TextWrapping.Wrap,
                        Width = maxTextW,
                        MaxWidth = maxTextW,
                        Foreground = brush,
                        FontSize = fs,
                        FontFamily = new FontFamily(string.IsNullOrEmpty(a.FontFamily) ? "Arial" : a.FontFamily),
                        FontWeight = a.Bold ? FontWeights.Bold : FontWeights.Normal,
                        FontStyle = a.Italic ? FontStyles.Italic : FontStyles.Normal,
                        TextDecorations = a.Underline ? TextDecorations.Underline : null,
                        TextAlignment = a.Align switch
                        {
                            TextAlign.Center => TextAlignment.Center,
                            TextAlign.Right => TextAlignment.Right,
                            TextAlign.Justify => TextAlignment.Justify,
                            _ => TextAlignment.Left
                        },
                        Tag = a
                    };
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
            // Click on empty space → deselect
            MainVM.SelectedAnnotation = null;
            Rebuild();
            return;
        }

        _dragStart = pos;

        if (tool == ToolMode.StickyNote)
        {
            var text = PromptDialog.Ask("Sticky Note", "Note text:");
            if (!string.IsNullOrWhiteSpace(text))
            {
                var note = new PdfAnnotation
                {
                    PageIndex = Page.PageIndex, Kind = AnnotationKind.StickyNote,
                    X = nx, Y = ny, Width = 0.02, Height = 0.02,
                    Color = MainVM.CurrentColor, Text = text
                };
                Page.Annotations.Add(note);
                // One-shot tool: revert to Select so the next click drags/edits the note
                // instead of dropping another one.
                MainVM.SelectedAnnotation = note;
                MainVM.CurrentTool = ToolMode.Select;
            }
            return;
        }

        if (tool == ToolMode.TextStamp)
        {
            var hex = "#" + MainVM.CurrentColor.R.ToString("X2") + MainVM.CurrentColor.G.ToString("X2") + MainVM.CurrentColor.B.ToString("X2");
            var r = TextStampDialog.Show(
                defaultFont: MainVM.CurrentFontFamily,
                defaultSize: MainVM.CurrentFontSize,
                defaultBold: MainVM.CurrentBold,
                defaultItalic: MainVM.CurrentItalic,
                defaultUnderline: MainVM.CurrentUnderline,
                defaultColorHex: hex,
                defaultAlign: MainVM.CurrentAlign);
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
                        Bold = r.Bold, Italic = r.Italic, Underline = r.Underline,
                        Align = r.Align
                    };
                    Page.Annotations.Add(stamp);
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
                FontFamily = "Segoe UI Symbol", FontSize = 18, Bold = true
            };
            Page.Annotations.Add(mark);
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
                ToolMode.SelectText or ToolMode.SelectImage => AnnotationKind.Rectangle, // draft preview only
                _ => AnnotationKind.Rectangle
            },
            X = nx, Y = ny, Width = 0, Height = 0,
            Color = tool is ToolMode.SelectText or ToolMode.SelectImage
                ? System.Windows.Media.Colors.DodgerBlue
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

        if (_resizingAnnotation != null && _resizingHandle != null)
        {
            var pw = Page.PixelWidth > 0 ? Page.PixelWidth : ActualWidth;
            var ph = Page.PixelHeight > 0 ? Page.PixelHeight : ActualHeight;
            var mp = e.GetPosition(this);
            var nx = System.Math.Clamp(mp.X / pw, 0, 1);
            var ny = System.Math.Clamp(mp.Y / ph, 0, 1);
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
            }
            Rebuild();
            return;
        }

        if (_movingAnnotation != null)
        {
            var pw = Page.PixelWidth > 0 ? Page.PixelWidth : ActualWidth;
            var ph = Page.PixelHeight > 0 ? Page.PixelHeight : ActualHeight;
            var mp = e.GetPosition(this);
            _movingAnnotation.X = System.Math.Clamp(mp.X / pw - _moveOffsetInPage.X, 0, 1 - System.Math.Max(0.005, _movingAnnotation.Width));
            _movingAnnotation.Y = System.Math.Clamp(mp.Y / ph - _moveOffsetInPage.Y, 0, 1 - System.Math.Max(0.005, _movingAnnotation.Height));
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

        if (_draftingVisual != null) Children.Remove(_draftingVisual);
        _draftingVisual = null;

        // Special tools consume the draft rectangle instead of committing it as an annotation.
        var currentTool = MainVM?.CurrentTool;
        if (currentTool == ToolMode.SelectText || currentTool == ToolMode.SelectImage)
        {
            if (ok)
            {
                var region = (_drafting.X, _drafting.Y, _drafting.Width, _drafting.Height);
                var pageIdx = Page!.PageIndex;
                var kind = currentTool.Value;
                _drafting = null;
                Rebuild();
                Dispatcher.BeginInvoke(new System.Action(() => InvokeRegionAction(pageIdx, region, kind)),
                    System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }
            _drafting = null;
            Rebuild();
            return;
        }

        if (ok) Page!.Annotations.Add(_drafting);
        else Rebuild();

        _drafting = null;
    }

    private void InvokeRegionAction(int pageIndex, (double X, double Y, double W, double H) region, ToolMode tool)
    {
        if (MainVM is null) return;
        MainVM.HandleRegionSelection(pageIndex, region.X, region.Y, region.W, region.H, tool);
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
