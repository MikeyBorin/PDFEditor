using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PDFEditor.Services;

namespace PDFEditor.Controls;

/// <summary>
/// Manage saved signatures and optionally pick one to place. Returns the picked entry, or null if cancelled.
/// </summary>
public static class SignatureLibraryDialog
{
    public static SignatureEntry? ShowAndPick(SignatureLibraryService library)
    {
        var w = new Window
        {
            Title = "Signatures",
            Width = 620, Height = 480,
            Owner = Application.Current?.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var addFromFile = new Button { Content = "+ From File...", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        var addDraw = new Button { Content = "+ Draw New...", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        var rename = new Button { Content = "Rename", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        var delete = new Button { Content = "Delete", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        top.Children.Add(addFromFile); top.Children.Add(addDraw); top.Children.Add(rename); top.Children.Add(delete);
        DockPanel.SetDock(top, Dock.Top);

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var place = new Button { Content = "Place on Current Page", Padding = new Thickness(12, 4, 12, 4), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "Close", Padding = new Thickness(12, 4, 12, 4), IsCancel = true };
        bottom.Children.Add(place); bottom.Children.Add(close);
        DockPanel.SetDock(bottom, Dock.Bottom);

        var list = new ListBox
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        list.ItemTemplate = BuildItemTemplate();

        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(list);
        w.Content = root;

        void Refresh()
        {
            list.ItemsSource = null;
            list.ItemsSource = library.List();
            if (list.Items.Count > 0 && list.SelectedItem == null) list.SelectedIndex = 0;
        }
        Refresh();

        addFromFile.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "Signature image (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
            if (dlg.ShowDialog() != true) return;
            var name = PromptDialog.Ask("Name signature", "Give it a name:", Path.GetFileNameWithoutExtension(dlg.FileName));
            if (string.IsNullOrWhiteSpace(name)) return;
            try { library.AddFromFile(dlg.FileName, name); Refresh(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Add failed"); }
        };

        addDraw.Click += (_, _) =>
        {
            var tempPath = SignatureCaptureDialog.Show();
            if (string.IsNullOrEmpty(tempPath) || !File.Exists(tempPath)) return;
            var name = PromptDialog.Ask("Name signature", "Give it a name:", "Signature " + DateTime.Now.ToString("yyyy-MM-dd"));
            if (string.IsNullOrWhiteSpace(name)) { try { File.Delete(tempPath); } catch { } return; }
            try
            {
                var bytes = File.ReadAllBytes(tempPath);
                library.AddFromPngBytes(bytes, name);
                try { File.Delete(tempPath); } catch { }
                Refresh();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Save failed"); }
        };

        rename.Click += (_, _) =>
        {
            if (list.SelectedItem is not SignatureEntry sel) return;
            var name = PromptDialog.Ask("Rename signature", "New name:", sel.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            library.Rename(sel.Id, name);
            Refresh();
        };

        delete.Click += (_, _) =>
        {
            if (list.SelectedItem is not SignatureEntry sel) return;
            if (MessageBox.Show($"Delete signature '{sel.Name}'?", "Confirm", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            library.Remove(sel.Id);
            Refresh();
        };

        SignatureEntry? picked = null;
        place.Click += (_, _) =>
        {
            if (list.SelectedItem is SignatureEntry sel) { picked = sel; w.DialogResult = true; }
            else MessageBox.Show("Select a signature first.", "PDF Editor");
        };

        return w.ShowDialog() == true ? picked : null;
    }

    private static DataTemplate BuildItemTemplate()
    {
        var xaml = @"
<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
              xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
              xmlns:svc='clr-namespace:PDFEditor.Services;assembly=PDFEditor'>
  <Border Padding='6' Margin='2' BorderBrush='#CCC' BorderThickness='0,0,0,1'>
    <StackPanel Orientation='Horizontal'>
      <Border BorderBrush='#999' BorderThickness='1' Width='120' Height='40' Background='White'>
        <Image Source='{Binding FileName, Converter={StaticResource SigPathConverter}}' Stretch='Uniform'/>
      </Border>
      <StackPanel Margin='10,0,0,0' VerticalAlignment='Center'>
        <TextBlock Text='{Binding Name}' FontWeight='SemiBold' FontSize='14'/>
        <TextBlock Text='{Binding Added, StringFormat=Added {0:yyyy-MM-dd HH:mm}}' Foreground='Gray' FontSize='11'/>
      </StackPanel>
    </StackPanel>
  </Border>
</DataTemplate>";
        var context = new System.Windows.Markup.ParserContext();
        context.XmlnsDictionary.Add("", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        context.XmlnsDictionary.Add("x", "http://schemas.microsoft.com/winfx/2006/xaml");
        return (DataTemplate)System.Windows.Markup.XamlReader.Parse(xaml, context);
    }
}

public class SigPathConverter : System.Windows.Data.IValueConverter
{
    public static SignatureLibraryService? Library { get; set; }

    public object? Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is string relPath && Library != null)
        {
            var full = Path.Combine(Library.LibraryDir, relPath);
            if (File.Exists(full))
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.UriSource = new Uri(full);
                img.EndInit();
                img.Freeze();
                return img;
            }
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
