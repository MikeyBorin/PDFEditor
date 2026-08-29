using System.Windows;
using System.Windows.Controls;

namespace PDFEditor.Controls;

public class PasswordSettings
{
    public string? UserPassword { get; set; }
    public string? OwnerPassword { get; set; }
    public bool PermitPrint { get; set; } = true;
    public bool PermitCopy { get; set; } = true;
    public bool PermitAnnotations { get; set; } = true;
    public bool PermitModify { get; set; } = true;
}

public static class PasswordDialog
{
    public static PasswordSettings? Show()
    {
        var w = new Window
        {
            Title = "Set Password Security",
            Width = 440, Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = "User password (required to open the PDF):", Margin = new Thickness(0, 0, 0, 4) });
        var userPw = new PasswordBox();
        root.Children.Add(userPw);
        root.Children.Add(new TextBlock { Text = "Owner password (required to change security):", Margin = new Thickness(0, 10, 0, 4) });
        var ownerPw = new PasswordBox();
        root.Children.Add(ownerPw);

        root.Children.Add(new TextBlock { Text = "Permissions:", Margin = new Thickness(0, 14, 0, 4), FontWeight = FontWeights.Bold });
        var cbPrint = new CheckBox { Content = "Allow printing", IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };
        var cbCopy = new CheckBox { Content = "Allow copy/extract of text and images", IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };
        var cbAnnot = new CheckBox { Content = "Allow annotations", IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };
        var cbModify = new CheckBox { Content = "Allow document modification", IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };
        root.Children.Add(cbPrint); root.Children.Add(cbCopy); root.Children.Add(cbAnnot); root.Children.Add(cbModify);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "Apply", Width = 80, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, Height = 28, IsCancel = true };
        btns.Children.Add(ok); btns.Children.Add(cancel);
        root.Children.Add(btns);

        w.Content = root;
        PasswordSettings? result = null;
        ok.Click += (_, _) =>
        {
            result = new PasswordSettings
            {
                UserPassword = string.IsNullOrEmpty(userPw.Password) ? null : userPw.Password,
                OwnerPassword = string.IsNullOrEmpty(ownerPw.Password) ? null : ownerPw.Password,
                PermitPrint = cbPrint.IsChecked == true,
                PermitCopy = cbCopy.IsChecked == true,
                PermitAnnotations = cbAnnot.IsChecked == true,
                PermitModify = cbModify.IsChecked == true,
            };
            w.DialogResult = true;
        };
        return w.ShowDialog() == true ? result : null;
    }
}
