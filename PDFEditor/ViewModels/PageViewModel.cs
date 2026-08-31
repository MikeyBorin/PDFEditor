using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PDFEditor.Models;

namespace PDFEditor.ViewModels;

public partial class PageViewModel : ObservableObject
{
    public int PageIndex { get; }

    [ObservableProperty] private BitmapSource? pageImage;
    [ObservableProperty] private BitmapSource? thumbnail;
    [ObservableProperty] private double pixelWidth;
    [ObservableProperty] private double pixelHeight;
    // True PDF page dimensions in points (1/72 inch). Used to compute
    // display size at Actual Size / Custom % / fit modes.
    [ObservableProperty] private double widthPt;
    [ObservableProperty] private double heightPt;
    [ObservableProperty] private bool isSelected;

    public ObservableCollection<PdfAnnotation> Annotations { get; } = new();

    public string DisplayNumber => $"{PageIndex + 1}";

    public event System.Action? AnnotationChanged;
    public void RaiseAnnotationChanged() => AnnotationChanged?.Invoke();

    public PageViewModel(int pageIndex) { PageIndex = pageIndex; }
}
