using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace Minimemizer;

internal sealed class ZoneOverlayWindow : Window
{
    private readonly Forms.Screen _screen;
    private readonly nint _taskViewOwner;
    private readonly bool _isDark = AppTheme.IsDarkModeEnabled();
    private readonly Dictionary<ScreenCorner, Border> _targets = [];

    internal ZoneOverlayWindow(Forms.Screen screen, AppLanguage language, nint taskViewOwner)
    {
        _screen = screen;
        _taskViewOwner = taskViewOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var area = screen.WorkingArea;
        Left = area.Left;
        Top = area.Top;
        Width = area.Width;
        Height = area.Height;

        var grid = new Grid { Background = Brushes.Transparent };
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        AddTarget(grid, language, ScreenCorner.TopLeft, 0, 0, HorizontalAlignment.Left, VerticalAlignment.Top);
        AddTarget(grid, language, ScreenCorner.TopRight, 0, 1, HorizontalAlignment.Right, VerticalAlignment.Top);
        AddTarget(grid, language, ScreenCorner.BottomLeft, 1, 0, HorizontalAlignment.Left, VerticalAlignment.Bottom);
        AddTarget(grid, language, ScreenCorner.BottomRight, 1, 1, HorizontalAlignment.Right, VerticalAlignment.Bottom);
        Content = grid;

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(HitTestHook);
            var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
            style = (style | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT) & ~NativeMethods.WS_EX_APPWINDOW;
            NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, new nint(style));
            NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWLP_HWNDPARENT, _taskViewOwner);
            NativeMethods.SetWindowPos(handle, 0, area.Left, area.Top, area.Width, area.Height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);
        };
    }

    private static nint HitTestHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        const int WmNcHitTest = 0x0084;
        const int HtTransparent = -1;
        if (message != WmNcHitTest) return 0;
        handled = true;
        return new nint(HtTransparent);
    }

    internal string ScreenDeviceName => _screen.DeviceName;

    internal void Highlight(ScreenCorner? corner)
    {
        foreach (var pair in _targets)
        {
            var active = pair.Key == corner;
            pair.Value.Background = new SolidColorBrush(active
                ? _isDark ? Color.FromArgb(175, 66, 66, 66) : Color.FromArgb(175, 205, 205, 205)
                : _isDark ? Color.FromArgb(105, 34, 34, 34) : Color.FromArgb(120, 238, 238, 238));
            pair.Value.BorderBrush = new SolidColorBrush(active
                ? _isDark ? Color.FromArgb(175, 150, 150, 150) : Color.FromArgb(170, 105, 105, 105)
                : _isDark ? Color.FromArgb(90, 118, 118, 118) : Color.FromArgb(100, 105, 105, 105));
            pair.Value.Opacity = active ? .82 : .48;
            if (pair.Value.Child is TextBlock label)
                label.Foreground = active || _isDark ? Brushes.White : new SolidColorBrush(Color.FromRgb(38, 38, 38));
        }
    }

    private void AddTarget(Grid grid, AppLanguage language, ScreenCorner corner, int row, int column,
        HorizontalAlignment horizontal, VerticalAlignment vertical)
    {
        var target = new Border
        {
            Width = 180,
            Height = 76,
            Margin = new Thickness(20),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            Child = new TextBlock
            {
                Text = $"{ThumbnailZones.DisplayLabel(language, _screen.DeviceName, Forms.Screen.AllScreens)}\n{ThumbnailZones.CornerLabel(language, corner)}",
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetRow(target, row);
        Grid.SetColumn(target, column);
        grid.Children.Add(target);
        _targets[corner] = target;
        Highlight(null);
    }
}
