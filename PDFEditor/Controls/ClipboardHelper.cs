using System.Threading;
using System.Windows;

namespace PDFEditor.Controls;

/// <summary>
/// WPF Clipboard operations. The classic CLIPBRD_E_CANT_OPEN error we saw was fired by
/// OleFlushClipboard (the "make data survive after we exit" step triggered by copy: true),
/// NOT by the initial OleSetClipboard. So we split the two: put the data on the clipboard
/// with copy: false (always works in the common case), then attempt Flush separately with
/// the error swallowed. The user sees their copy land immediately with no modal dialog.
/// Persistence-after-app-exit is best-effort — usually fine, and no worse than not copying
/// at all.
/// </summary>
public static class ClipboardHelper
{
    // The primary OleSetClipboard is very rarely blocked; a short retry covers the
    // occasional window during which some other app briefly holds the clipboard.
    private const int PrimaryAttempts = 3;
    private const int PrimaryDelayMs = 50;

    public static void SetText(string text)
    {
        var data = new DataObject();
        data.SetText(text ?? "");
        SetWithRetry(data);
        TryFlush();
    }

    public static void SetImage(System.Windows.Media.Imaging.BitmapSource image)
    {
        var data = new DataObject();
        data.SetImage(image);
        SetWithRetry(data);
        TryFlush();
    }

    private static void SetWithRetry(DataObject data)
    {
        for (int i = 0; i < PrimaryAttempts; i++)
        {
            try
            {
                // copy: false → no flush, so this call is (almost) never the source of
                // CLIPBRD_E_CANT_OPEN. Data is on the clipboard for the current session.
                Clipboard.SetDataObject(data, copy: false);
                return;
            }
            catch (System.Runtime.InteropServices.ExternalException) when (i < PrimaryAttempts - 1)
            {
                Thread.Sleep(PrimaryDelayMs);
            }
        }
    }

    private static void TryFlush()
    {
        // Best-effort persistence: try to make the clipboard content survive PDFEditor
        // exiting. If this fails (which is what usually threw the modal error before),
        // the data is still on the clipboard for the current session — swallow it.
        try { Clipboard.Flush(); }
        catch (System.Runtime.InteropServices.ExternalException) { }
    }
}
