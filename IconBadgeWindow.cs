using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Minimemizer;

internal sealed class IconBadgeWindow : Window
{
    private const int BadgeSize = 38;
    private nint _handle;
    private readonly Window _owner;
    private readonly System.Windows.Controls.Image? _iconImage;

    internal IconBadgeWindow(Window owner, string executablePath)
    {
        _owner = owner;
        Owner = owner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Topmost = false;
        ShowActivated = false;
        IsHitTestVisible = false;
        Width = BadgeSize;
        Height = BadgeSize;

        var image = LoadIcon(executablePath);
        if (image is null) return;
        _iconImage = new System.Windows.Controls.Image { Source = image, Stretch = Stretch.Uniform };
        Content = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(0),
            BorderBrush = System.Windows.Media.Brushes.Transparent,
            Background = System.Windows.Media.Brushes.Transparent,
            Padding = new Thickness(4),
            Child = _iconImage
        };
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GWL_EXSTYLE).ToInt64();
            style = (style | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE) & ~NativeMethods.WS_EX_APPWINDOW;
            NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GWL_EXSTYLE, new nint(style));
        };
    }

    internal bool HasIcon => Content is not null;

    internal void ApplyPosition(int thumbnailX, int thumbnailY, int thumbnailWidth, int thumbnailHeight, ThumbnailIconPosition position, bool visible)
    {
        if (!HasIcon) return;
        var onRight = position is ThumbnailIconPosition.TopRight or ThumbnailIconPosition.BottomRight;
        var centered = position is ThumbnailIconPosition.TopCenter or ThumbnailIconPosition.BottomCenter;
        var onBottom = position is ThumbnailIconPosition.BottomLeft or ThumbnailIconPosition.BottomRight;
        onBottom = onBottom || position == ThumbnailIconPosition.BottomCenter;
        var iconX = centered ? thumbnailX + (thumbnailWidth - BadgeSize) / 2 : onRight ? thumbnailX + thumbnailWidth - BadgeSize - 7 : thumbnailX + 7;
        var iconY = onBottom ? thumbnailY + thumbnailHeight - BadgeSize - 7 : thumbnailY + 7;
        Left = iconX;
        Top = iconY;
        Width = BadgeSize;
        Height = BadgeSize;
        if (visible && !IsVisible) Show();
        else if (!visible && IsVisible) Hide();
        if (visible && _handle != 0)
            NativeMethods.SetWindowPos(_handle, 0, iconX, iconY, BadgeSize, BadgeSize, NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
    }

    internal void SetOpacity(int opacityPercent)
    {
        Opacity = 1;
        if (_iconImage is not null)
            _iconImage.Opacity = Math.Clamp(opacityPercent / 100d, 0.2d, 1d);
    }

    private static BitmapSource? LoadIcon(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            return source;
        }
        catch { return null; }
    }
}
