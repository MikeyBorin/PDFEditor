using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PDFEditor.Services;

namespace PDFEditor.Controls;

/// <summary>Little dialog for Help → Check for Updates. Shows current vs
/// latest version, a button to copy the installer URL to the clipboard, and
/// a button to open the release page in the default browser. Handles the
/// offline / error case by showing the error and still offering the browser
/// link (useful when the app can't reach GitHub but the default browser can
/// through a different network path or a whitelisted proxy).</summary>
public static class UpdateCheckDialog
{
    public static void Show(UpdateCheckResult r, string releasePageFallbackUrl)
    {
        var w = new Window
        {
            Title = "Check for Updates",
            Width = 520,
            SizeToContent = SizeToContent.Height,
            Owner = Application.Current?.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (Brush)Application.Current!.Resources["Bg"],
            Foreground = (Brush)Application.Current!.Resources["Text"]
        };

        var root = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };

        // -- Headline + body ----------------------------------------------
        string headline, body;
        var newer = r.NewerAvailable;
        if (r.ErrorMessage != null)
        {
            headline = "Couldn't reach GitHub";
            body =
                $"Current version: {r.CurrentVersion}\n\n" +
                "Update check failed. Common causes:\n" +
                "  • No internet connection right now.\n" +
                "  • This PC is on a network that blocks github.com.\n\n" +
                $"Error: {r.ErrorMessage}\n\n" +
                "You can check manually from any browser at the release page below.";
        }
        else if (newer)
        {
            var when = r.PublishedAt is null ? "" :
                $" (published {SafeDate(r.PublishedAt)})";
            headline = $"Update available: {r.LatestVersion}";
            body =
                $"You're on {r.CurrentVersion}.\n" +
                $"Latest release is {r.LatestVersion}{when}.\n\n" +
                "Download options:\n" +
                "  • Copy installer URL below and paste into a browser or download tool.\n" +
                "  • Open the release page in your browser to grab the file manually.\n\n" +
                "Note: some corporate networks allow browsing github.com but block " +
                "release-asset downloads from objects.githubusercontent.com. " +
                "If direct download fails at your office, grab the installer at home " +
                "and transfer via your usual channel (FTP, OneDrive, USB).";
        }
        else
        {
            headline = "You're up to date";
            body =
                $"Current version: {r.CurrentVersion}\n" +
                $"Latest release:  {r.LatestVersion}\n\n" +
                "No newer version is available on GitHub.";
        }

        root.Children.Add(new TextBlock
        {
            Text = headline,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });
        root.Children.Add(new TextBlock
        {
            Text = body,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });

        // -- Buttons ------------------------------------------------------
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0)
        };

        if (newer && !string.IsNullOrEmpty(r.InstallerDownloadUrl))
        {
            var copyInstaller = new Button
            {
                Content = "Copy installer URL",
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = r.InstallerDownloadUrl
            };
            copyInstaller.Click += (_, _) =>
            {
                try { ClipboardHelper.SetText(r.InstallerDownloadUrl!); }
                catch { /* clipboard occasionally fails; user can still open browser */ }
                copyInstaller.Content = "Copied ✓";
                copyInstaller.IsEnabled = false;
            };
            buttonRow.Children.Add(copyInstaller);
        }

        var pageUrl = r.ReleasePageUrl ?? releasePageFallbackUrl;
        var openBrowser = new Button
        {
            Content = newer ? "Open release page" : (r.ErrorMessage != null ? "Open release page in browser" : "Open release page"),
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = pageUrl
        };
        openBrowser.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(pageUrl) { UseShellExecute = true }); }
            catch { /* if browser launch fails, user can still copy the URL from tooltip */ }
        };
        buttonRow.Children.Add(openBrowser);

        var close = new Button
        {
            Content = "Close",
            Padding = new Thickness(12, 5, 12, 5),
            IsCancel = true,
            IsDefault = true
        };
        close.Click += (_, _) => w.DialogResult = true;
        buttonRow.Children.Add(close);

        root.Children.Add(buttonRow);
        w.Content = root;
        w.ShowDialog();
    }

    private static string SafeDate(string iso)
    {
        // Just the yyyy-mm-dd part; don't parse if the API sent something odd.
        return iso.Length >= 10 ? iso.Substring(0, 10) : iso;
    }
}
