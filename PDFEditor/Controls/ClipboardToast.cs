using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PDFEditor.Controls;

/// <summary>Transient bubble that briefly shows a snippet of what just landed
/// on the clipboard, so the user can confirm the copy happened and see what
/// was captured without opening a target app. Fades out on its own timer.</summary>
public static class ClipboardToast
{
    private const int PreviewChars = 140;
    private const int VisibleMs = 1800;

    private static Popup? _current;
    private static DispatcherTimer? _timer;

    public static void ShowText(string text)
    {
        var owner = Application.Current?.MainWindow;
        if (owner is null || string.IsNullOrEmpty(text)) return;

        // Collapse whitespace so multi-line snippets read as one running line.
        var preview = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        if (preview.Length > PreviewChars) preview = preview.Substring(0, PreviewChars) + "…";

        var header = new TextBlock
        {
            Text = $"Copied to clipboard  ({text.Length:N0} char{(text.Length == 1 ? "" : "s")})",
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4)
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");

        var body = new TextBlock
        {
            Text = preview,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, "Text");

        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(body);

        var border = new Border
        {
            Padding = new Thickness(12, 8, 12, 10),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Child = content
        };
        border.SetResourceReference(Border.BackgroundProperty, "MenuPopupBg");
        border.SetResourceReference(Border.BorderBrushProperty, "Accent");

        DismissCurrent();

        var popup = new Popup
        {
            PlacementTarget = owner,
            Placement = PlacementMode.Center,
            AllowsTransparency = true,
            StaysOpen = true,
            Child = border
        };

        _current = popup;
        popup.IsOpen = true;

        // Fade in, hold, fade out — driven from one storyboard so the two
        // animations don't clobber each other on the same property.
        border.Opacity = 0;
        var frames = new DoubleAnimationUsingKeyFrames();
        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        frames.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        frames.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(VisibleMs - 300))));
        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(VisibleMs))));
        border.BeginAnimation(UIElement.OpacityProperty, frames);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(VisibleMs) };
        _timer.Tick += (_, _) => DismissCurrent();
        _timer.Start();
    }

    private static void DismissCurrent()
    {
        _timer?.Stop();
        _timer = null;
        if (_current is not null)
        {
            _current.IsOpen = false;
            _current = null;
        }
    }
}
