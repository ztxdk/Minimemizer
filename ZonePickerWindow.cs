using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using MediaBrush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Point = System.Windows.Point;
using MessageBox = System.Windows.MessageBox;

namespace Minimemizer;

internal sealed class ZonePickerWindow : Window
{
    private static ZonePickerWindow? _openPicker;
    private readonly Action<ThumbnailZone> _selected;
    private readonly bool _dark = AppTheme.IsDarkModeEnabled();
    private bool _dismissArmed;
    private bool _closing;

    internal ZonePickerWindow(AppLanguage language, ThumbnailZone current, Action<ThumbnailZone> selected)
    {
        _selected = selected;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var surface = Brush(_dark ? 38 : 249);
        var border = Brush(_dark ? 78 : 204);
        var foreground = Brush(_dark ? 242 : 24);
        var muted = Brush(_dark ? 174 : 92);
        var panel = new WrapPanel
        {
            Margin = new Thickness(0, 10, 0, 0),
            Width = Math.Min(Forms.Screen.AllScreens.Length, 3) * 210
        };

        foreach (var screen in Forms.Screen.AllScreens)
        {
            var card = new StackPanel { Margin = new Thickness(5) };
            card.Children.Add(new TextBlock
            {
                Text = ThumbnailZones.DisplayLabel(language, screen.DeviceName, Forms.Screen.AllScreens) +
                       (screen.Primary ? $" · {Localizer.T(language, "Primær")}" : ""),
                Foreground = foreground,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(3, 0, 3, 7)
            });
            card.Children.Add(CreateCornerGrid(language, screen, current, foreground, muted));
            panel.Children.Add(card);
        }

        var root = new StackPanel { Margin = new Thickness(15) };
        root.Children.Add(new TextBlock
        {
            Text = Localizer.T(language, "Vælg skærm og hjørne"),
            Foreground = foreground,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold
        });
        root.Children.Add(new TextBlock
        {
            Text = Localizer.T(language, "Klik på det hjørne, thumbnails skal placeres i."),
            Foreground = muted,
            Margin = new Thickness(0, 3, 0, 0)
        });
        root.Children.Add(panel);

        Content = new Border
        {
            Background = surface,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(0),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 4,
                Opacity = _dark ? .45 : .22
            },
            Child = root
        };

        Closing += (_, _) => _closing = true;
        Deactivated += (_, _) =>
        {
            if (_dismissArmed && !_closing) Close();
        };
        Closed += (_, _) =>
        {
            if (ReferenceEquals(_openPicker, this)) _openPicker = null;
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
            NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE,
                new nint((style | NativeMethods.WS_EX_TOOLWINDOW) & ~NativeMethods.WS_EX_APPWINDOW));
            var dark = _dark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        };
    }

    private Grid CreateCornerGrid(AppLanguage language, Forms.Screen screen, ThumbnailZone current, MediaBrush foreground, MediaBrush muted)
    {
        var grid = new Grid { Width = 190, Height = 112 };
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        AddCorner(grid, language, screen, ScreenCorner.TopLeft, 0, 0, current, foreground, muted);
        AddCorner(grid, language, screen, ScreenCorner.TopRight, 0, 1, current, foreground, muted);
        AddCorner(grid, language, screen, ScreenCorner.BottomLeft, 1, 0, current, foreground, muted);
        AddCorner(grid, language, screen, ScreenCorner.BottomRight, 1, 1, current, foreground, muted);
        return grid;
    }

    private void AddCorner(Grid grid, AppLanguage language, Forms.Screen screen, ScreenCorner corner, int row, int column,
        ThumbnailZone current, MediaBrush foreground, MediaBrush muted)
    {
        var selected = string.Equals(current.ScreenDeviceName, screen.DeviceName, StringComparison.OrdinalIgnoreCase) && current.Corner == corner;
        var normal = Brush(_dark ? (selected ? 77 : 48) : (selected ? 218 : 242));
        var hover = Brush(_dark ? 68 : 226);
        var line = Brush(_dark ? (selected ? 154 : 82) : (selected ? 112 : 202));
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = (selected ? "✓  " : "") + ThumbnailZones.CornerLabel(language, corner),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Foreground = selected ? foreground : muted,
                FontSize = 11.5,
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal
            },
            Background = normal,
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(2),
            Padding = new Thickness(5),
            Cursor = Cursors.Hand,
            Tag = selected
        };
        button.Template = CreateButtonTemplate();
        button.MouseEnter += (_, _) => button.Background = hover;
        button.MouseLeave += (_, _) => button.Background = normal;
        button.Click += (_, _) =>
        {
            Close();
            try { _selected(new ThumbnailZone(screen.DeviceName, corner)); }
            catch (Exception ex)
            {
                MessageBox.Show($"{Localizer.T(language, "Zonen kunne ikke ændres:")}\n{ex.Message}", "Minimemizer",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        Grid.SetRow(button, row);
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
    }

    private static ControlTemplate CreateButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    internal static void ShowForAnchor(FrameworkElement anchor, AppLanguage language, ThumbnailZone current,
        Action<ThumbnailZone> selected)
    {
        Point physicalPoint;
        DpiScale dpi;
        try
        {
            physicalPoint = anchor.PointToScreen(new Point(0, anchor.ActualHeight));
            dpi = VisualTreeHelper.GetDpi(anchor);
        }
        catch (InvalidOperationException) { return; }
        var screen = Forms.Screen.FromPoint(new System.Drawing.Point((int)physicalPoint.X, (int)physicalPoint.Y));
        var point = new Point(physicalPoint.X / dpi.DpiScaleX, physicalPoint.Y / dpi.DpiScaleY);

        anchor.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            try
            {
                _openPicker?.Close();
                var picker = new ZonePickerWindow(language, current, selected);
                _openPicker = picker;
                picker.ShowAt(point, screen, dpi);
            }
            catch (Exception ex)
            {
                _openPicker = null;
                MessageBox.Show($"{Localizer.T(language, "Zonevælgeren kunne ikke åbnes:")}\n{ex.Message}", "Minimemizer",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        });
    }

    private void ShowAt(Point point, Forms.Screen screen, DpiScale dpi)
    {
        Left = point.X;
        Top = point.Y + 4;
        Show();
        UpdateLayout();
        var workLeft = screen.WorkingArea.Left / dpi.DpiScaleX;
        var workTop = screen.WorkingArea.Top / dpi.DpiScaleY;
        var workRight = screen.WorkingArea.Right / dpi.DpiScaleX;
        var workBottom = screen.WorkingArea.Bottom / dpi.DpiScaleY;
        Left = Math.Clamp(point.X, workLeft + 8, Math.Max(workLeft + 8, workRight - ActualWidth - 8));
        Top = Math.Clamp(point.Y + 4, workTop + 8, Math.Max(workTop + 8, workBottom - ActualHeight - 8));
        Activate();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            if (!IsVisible) return;
            _dismissArmed = true;
            if (!IsActive) Activate();
        });
    }

    private static SolidColorBrush Brush(int value)
    {
        var brush = new SolidColorBrush(Color.FromRgb((byte)value, (byte)value, (byte)value));
        brush.Freeze();
        return brush;
    }
}
