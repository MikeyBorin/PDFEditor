using CommunityToolkit.Mvvm.ComponentModel;

namespace PDFEditor.ViewModels;

/// <summary>Single row in the OCR Languages submenu. IsCurrent binds to
/// <c>MenuItem.IsChecked</c> (visible tick); Header gets a suffix "(current)"
/// or "(installed)" so the state is readable even in high-contrast themes.</summary>
public partial class OcrLanguageItem : ObservableObject
{
    public string Code { get; }
    public string DisplayName { get; }

    [ObservableProperty] private bool isInstalled;
    [ObservableProperty] private bool isCurrent;

    public OcrLanguageItem(string code, string displayName)
    {
        Code = code;
        DisplayName = displayName;
    }

    public string Header =>
        IsCurrent   ? $"✓  {DisplayName}   (current)" :
        IsInstalled ? $"    {DisplayName}   (installed)" :
                      $"    {DisplayName}";

    partial void OnIsInstalledChanged(bool value) => OnPropertyChanged(nameof(Header));
    partial void OnIsCurrentChanged(bool value)   => OnPropertyChanged(nameof(Header));
}
