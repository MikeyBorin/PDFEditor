using System.Threading;
using System.Windows;

namespace PDFEditor.Controls;

/// <summary>
/// WPF Clipboard operations that retry on the classic "OpenClipboard Failed" transient error
/// caused by other apps holding the clipboard.
/// </summary>
public static class ClipboardHelper
{
    private const int MaxAttempts = 10;
    private const int DelayMs = 100;

    public static void SetText(string text)
    {
        for (int i = 0; i < MaxAttempts; i++)
        {
            try
            {
                var data = new DataObject();
                data.SetText(text ?? "");
                Clipboard.SetDataObject(data, copy: true);
                return;
            }
            catch (System.Runtime.InteropServices.COMException) when (i < MaxAttempts - 1) { Thread.Sleep(DelayMs); }
            catch (System.Runtime.InteropServices.ExternalException) when (i < MaxAttempts - 1) { Thread.Sleep(DelayMs); }
        }
    }

    public static void SetImage(System.Windows.Media.Imaging.BitmapSource image)
    {
        for (int i = 0; i < MaxAttempts; i++)
        {
            try
            {
                var data = new DataObject();
                data.SetImage(image);
                Clipboard.SetDataObject(data, copy: true);
                return;
            }
            catch (System.Runtime.InteropServices.COMException) when (i < MaxAttempts - 1) { Thread.Sleep(DelayMs); }
            catch (System.Runtime.InteropServices.ExternalException) when (i < MaxAttempts - 1) { Thread.Sleep(DelayMs); }
        }
    }
}
