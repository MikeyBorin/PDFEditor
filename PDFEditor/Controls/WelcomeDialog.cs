using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace PDFEditor.Controls;

/// <summary>
/// First-run welcome + disclaimer. Shown once per user, then never again — the
/// acknowledgement is persisted as a marker file under %AppData%\ArtiMaxPDFEditor\.
/// Soft "Got it" dismissal (single button) rather than an Accept/Decline gate;
/// the PolyForm Noncommercial licence applies regardless of click-through, so
/// the dialog exists for reassurance and transparency, not legal enforcement.
/// </summary>
public static class WelcomeDialog
{
    private static string AckPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ArtiMaxPDFEditor",
        "welcome.ack");

    public static bool AlreadyAcknowledged() => File.Exists(AckPath);

    /// <summary>Show the welcome dialog. Returns true if user acknowledged; the marker
    /// file is written in that case. Currently there is no "Decline" outcome — we're
    /// not gating startup on acceptance — but callers can react to false if we ever add one.</summary>
    public static bool ShowOnce()
    {
        if (AlreadyAcknowledged()) return true;

        var w = new Window
        {
            Title = "Welcome to ArtiMax PDF Editor",
            Width = 620,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (Brush)Application.Current!.Resources["Bg"],
            Foreground = (Brush)Application.Current!.Resources["Text"]
        };

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 18) };

        root.Children.Add(new TextBlock
        {
            Text = "Welcome to ArtiMax PDF Editor",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Free for non-commercial use · v1.0.2 · by ArtiMax",
            Opacity = 0.7,
            Margin = new Thickness(0, 0, 0, 18)
        });

        // Warm intro
        root.Children.Add(new TextBlock
        {
            Text = "Thanks for trying it. Everything runs locally on your PC — no telemetry, " +
                   "no cloud upload, no account required. Press F1 at any time for the full help.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        // Disclaimer block, softly styled but visually distinct
        var warnBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(30, 214, 138, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(200, 214, 138, 0)),
            BorderThickness = new Thickness(0, 0, 0, 0),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 18)
        };
        var warn = new StackPanel();
        warn.Children.Add(new TextBlock
        {
            Text = "BEFORE YOU START",
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0x82, 0x00)),
            Margin = new Thickness(0, 0, 0, 6),
            FontSize = 12
        });
        warn.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Inlines =
            {
                new Run("This software is provided "),
                new Run("\"AS IS\", ") { FontWeight = FontWeights.SemiBold },
                new Run("without warranty of any kind. There is no guarantee it is fit " +
                        "for any particular purpose. ArtiMax accepts no liability for " +
                        "data loss, corrupted files, missed information, incorrect edits, " +
                        "or any other damages arising from use of this software."),
                new LineBreak(), new LineBreak(),
                new Run("Always keep backups of important documents before editing.") { FontWeight = FontWeights.SemiBold },
                new LineBreak(), new LineBreak(),
                new Run("See the LICENSE file (PolyForm Noncommercial License 1.0.0) for full terms. Help → About also shows this text at any time."),
            }
        });
        warnBorder.Child = warn;
        root.Children.Add(warnBorder);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var got = new Button
        {
            Content = "Got it — don't show this again",
            IsDefault = true,
            Padding = new Thickness(16, 6, 16, 6),
            MinWidth = 220
        };
        buttons.Children.Add(got);
        root.Children.Add(buttons);

        bool acknowledged = false;
        got.Click += (_, _) =>
        {
            acknowledged = true;
            w.DialogResult = true;
        };

        w.Content = root;
        w.ShowDialog();

        if (acknowledged)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AckPath)!);
                File.WriteAllText(AckPath, "acknowledged " + DateTime.UtcNow.ToString("o"));
            }
            catch { /* best-effort persistence */ }
        }
        return acknowledged;
    }
}
