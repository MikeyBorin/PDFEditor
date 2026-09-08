using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PDFEditor.ViewModels;

namespace PDFEditor.Controls;

// Shared colour palette popup — used by both the main toolbar colour button
// and the floating Tool Palette's swatch cell so both surfaces stay in sync.
public static class ColourSwatchPopup
{
    public static readonly (string Name, Color Colour)[] Palette = new[]
    {
        ("Black",  Colors.Black),
        ("Grey",   Color.FromRgb(0x80, 0x80, 0x80)),
        ("Red",    Colors.Red),
        ("Orange", Color.FromRgb(0xFF, 0xA5, 0x00)),
        ("Yellow", Colors.Yellow),
        ("Green",  Color.FromRgb(0x2E, 0x8B, 0x2E)),
        ("Cyan",   Color.FromRgb(0x00, 0xB7, 0xC3)),
        ("Blue",   Color.FromRgb(0x1F, 0x6F, 0xEB)),
        ("Purple", Color.FromRgb(0x8B, 0x5C, 0xF6)),
        ("Pink",   Color.FromRgb(0xE9, 0x1E, 0x63)),
        ("Brown",  Color.FromRgb(0x8B, 0x45, 0x13)),
        ("White",  Colors.White),
    };

    public static void Show(FrameworkElement anchor, MainViewModel vm)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = anchor,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false,
        };

        var grid = new System.Windows.Controls.Primitives.UniformGrid
        {
            Columns = 6, Rows = 2, Width = 180
        };
        foreach (var (name, col) in Palette)
        {
            var cell = new Button
            {
                Width = 26, Height = 26, Margin = new Thickness(1),
                Background = new SolidColorBrush(col),
                BorderBrush = System.Windows.Media.Brushes.Gray, BorderThickness = new Thickness(1),
                ToolTip = name, Cursor = Cursors.Hand
            };
            var captured = col;
            cell.Click += (_, _) =>
            {
                vm.CurrentColor = captured;
                menu.IsOpen = false;
            };
            grid.Children.Add(cell);
        }
        var gridItem = new MenuItem
        {
            Header = grid,
            StaysOpenOnClick = true,
            Padding = new Thickness(6)
        };
        menu.Items.Add(gridItem);
        menu.Items.Add(new Separator());
        var more = new MenuItem { Header = "More Colours..." };
        more.Click += (_, _) =>
        {
            menu.IsOpen = false;
            vm.PickColourCommand.Execute(null);
        };
        menu.Items.Add(more);

        anchor.ContextMenu = menu;
        menu.IsOpen = true;
    }
}
