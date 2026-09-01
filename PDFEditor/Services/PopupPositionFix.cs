using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace PDFEditor.Services;

/// <summary>
/// Workaround for a WPF popup-positioning bug that manifests in this project
/// as "Font / Size ComboBoxes in TextStampDialog jump to screen top-left after
/// a PDF is loaded", plus "Text-Tool and Draw-Tool drop-downs jump to top-left".
///
/// Symptom (captured by PopupDiag): PlacementTarget's PointToScreen returns
/// correct coords (e.g. 1336, 448) but the popup's HWND lands at (0,0). Only
/// affects popups whose PlacementTarget lives in a *separate* Window (dialog),
/// not popups in the main window. Root cause is a DPI-context mismatch between
/// WPF (System-DPI-Aware) and the popup's Win32 HWND placement path on multi-
/// monitor setups.
///
/// The fix: after the popup opens, "poke" its HorizontalOffset — assign
/// oldValue + tinyDelta then back to oldValue. This forces WPF's Popup to
/// re-run its positioning code, which the second time round produces the
/// correct coordinates. A widely-cited WPF workaround.
/// </summary>
internal static class PopupPositionFix
{
    public static void Install()
    {
        // ContextMenu poke: fixes Text-Tool / Draw-Tool split-button drop-downs
        // that would otherwise open at desktop (0,0) after a PDF is loaded.
        EventManager.RegisterClassHandler(
            typeof(ContextMenu),
            ContextMenu.OpenedEvent,
            new RoutedEventHandler(OnContextMenuOpened));

        // ComboBox detect-and-fix: some ComboBox popups (specifically those
        // inside dialog windows, like Font / Size in TextStampDialog) open at
        // desktop (0,0) after a PDF is loaded. Detect the misplacement and
        // re-position the popup HWND to sit directly below the ComboBox.
        EventManager.RegisterClassHandler(
            typeof(ComboBox),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) =>
            {
                if (s is ComboBox cb)
                {
                    cb.DropDownOpened -= OnComboDropDownOpened;
                    cb.DropDownOpened += OnComboDropDownOpened;
                }
            }));

        // MenuItem submenu detect-and-fix: nested submenus (File > Export,
        // View > Theme, Tools > Security, File > Open Recent, etc.) can also
        // land at (0,0) after a PDF is loaded. Same detect-and-reposition
        // approach — use the popup's Placement direction to compute the
        // correct location relative to the target MenuItem.
        EventManager.RegisterClassHandler(
            typeof(MenuItem),
            MenuItem.SubmenuOpenedEvent,
            new RoutedEventHandler(OnMenuItemSubmenuOpened));
    }

    private static void OnComboDropDownOpened(object? sender, EventArgs e)
    {
        if (sender is not ComboBox cb) return;
        cb.Dispatcher.BeginInvoke(new Action(() =>
        {
            var popup = FindPopupInside(cb);
            if (popup == null) return;
            // Directly below the ComboBox.
            var target = cb.PointToScreen(new Point(0, cb.ActualHeight));
            FixIfAtOrigin(popup, target);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static void OnMenuItemSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        mi.Dispatcher.BeginInvoke(new Action(() =>
        {
            var popup = FindPopupInside(mi);
            if (popup == null) return;
            // Placement Right = to the right of the MenuItem (nested submenu).
            // Placement Bottom = below (top-level menu on the menu bar).
            // Default here uses the popup's own Placement.
            Point target;
            if (popup.Placement == PlacementMode.Right)
                target = mi.PointToScreen(new Point(mi.ActualWidth, 0));
            else
                target = mi.PointToScreen(new Point(0, mi.ActualHeight));
            FixIfAtOrigin(popup, target);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static Popup? FindPopupInside(Control control)
    {
        try
        {
            control.ApplyTemplate();
            return control.Template?.FindName("PART_Popup", control) as Popup;
        }
        catch { return null; }
    }

    /// <summary>If the popup's HWND landed at desktop (0,0) (the bug
    /// signature) and the intended target isn't at (0,0), reposition it.
    /// Correctly-positioned popups are left untouched.</summary>
    private static void FixIfAtOrigin(Popup popup, Point intendedTargetScreen)
    {
        try
        {
            if (popup.Child is not Visual pv) return;
            if (PresentationSource.FromVisual(pv) is not HwndSource psrc) return;
            if (!GetWindowRect(psrc.Handle, out var rect)) return;
            if (rect.Left != 0 || rect.Top != 0) return;
            if (intendedTargetScreen.X == 0 && intendedTargetScreen.Y == 0) return;
            SetWindowPos(psrc.Handle, IntPtr.Zero,
                (int)Math.Round(intendedTargetScreen.X),
                (int)Math.Round(intendedTargetScreen.Y),
                0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
        catch { }
    }

    private static void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu cm) PokeContextMenu(cm);
    }

    private static void PokeContextMenu(ContextMenu cm)
    {
        // ContextMenu isn't a Popup but exposes the same offset properties
        // (it owns a Popup internally and forwards to it). Nudging
        // HorizontalOffset by 1 device unit then back triggers WPF's internal
        // Reposition() path, which recomputes screen coords correctly on the
        // second run. Visually the user sees no glitch.
        try
        {
            var h = cm.HorizontalOffset;
            cm.HorizontalOffset = h + 1;
            cm.HorizontalOffset = h;
        }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    private const uint SWP_NOSIZE     = 0x0001;
    private const uint SWP_NOZORDER   = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
}
