using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using FontFamily = System.Windows.Media.FontFamily;

namespace Minimemizer;

internal sealed class TrayMenuWindow : Window
{
    internal TrayMenuWindow(AppLanguage language, Action openSettings, Action exit)
    {
        Width = 220;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = true;

        var dark = IsDarkMode();
        var background = new SolidColorBrush(dark ? Color.FromRgb(44, 44, 44) : Color.FromRgb(249, 249, 249));
        var foreground = new SolidColorBrush(dark ? Color.FromRgb(245, 245, 245) : Color.FromRgb(28, 28, 28));
        var hover = new SolidColorBrush(dark ? Color.FromRgb(62, 62, 62) : Color.FromRgb(232, 232, 232));
        var borderBrush = new SolidColorBrush(dark ? Color.FromRgb(82, 82, 82) : Color.FromRgb(205, 205, 205));

        var stack = new StackPanel { Margin = new Thickness(5) };
        stack.Children.Add(MenuButton("⚙", Localizer.T(language, "Indstillinger"), foreground, hover, () => { Close(); openSettings(); }));
        stack.Children.Add(new Border { Height = 1, Background = borderBrush, Margin = new Thickness(9, 4, 9, 4) });
        stack.Children.Add(MenuButton("⏻", Localizer.T(language, "Afslut"), foreground, hover, () => { Close(); exit(); }));

        Content = new Border
        {
            Background = background,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = dark ? .5 : .25 },
            Child = stack
        };
    }

    private static Button MenuButton(string glyph, string text, Brush foreground, Brush hover, Action action)
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 16, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
        var label = new TextBlock { Text = text, FontFamily = new FontFamily("Segoe UI"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1); content.Children.Add(label);
        var button = new Button { Content = content, Height = 42, Foreground = foreground, Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(5, 0, 10, 0) };
        var normal = Brushes.Transparent;
        button.MouseEnter += (_, _) => button.Background = hover;
        button.MouseLeave += (_, _) => button.Background = normal;
        button.Click += (_, _) => action();
        return button;
    }

    internal void ShowNearCursor()
    {
        Show();
        UpdateLayout();
        var cursor = Forms.Cursor.Position;
        var screen = Forms.Screen.FromPoint(cursor);
        var handle = new WindowInteropHelper(this).Handle;
        var scale = handle == 0 ? 1d : Math.Max(1d, NativeMethods.GetDpiForWindow(handle) / 96d);
        var physicalWidth = ActualWidth * scale;
        var physicalHeight = ActualHeight * scale;
        var x = Math.Clamp(cursor.X - physicalWidth, screen.WorkingArea.Left, screen.WorkingArea.Right - physicalWidth);
        var y = Math.Clamp(cursor.Y - physicalHeight - 6, screen.WorkingArea.Top, screen.WorkingArea.Bottom - physicalHeight);
        Left = x / scale;
        Top = y / scale;
        Activate();
        Deactivated += (_, _) => Close();
    }

    private static bool IsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch { return false; }
    }
}
