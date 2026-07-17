using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls;

namespace Minimemizer;

internal sealed class ThumbnailWindow : Window
{
    private readonly nint _source;
    private readonly uint _sourceProcessId;
    private readonly uint _sourceThreadId;
    private readonly nint _taskViewOwner;
    private nint _thumbnail;
    private nint _handle;
    private int _pixelWidth;
    private int _pixelHeight;
    private int _pixelX;
    private int _pixelY;
    private IconBadgeWindow? _iconBadge;
    private readonly string _executablePath;
    private bool _showIcon = true;
    private bool _restoreOnSingleClick;
    private ThumbnailFrameStyle _frameStyle;
    private ThumbnailIconPosition _iconPosition = ThumbnailIconPosition.TopRight;
    private int _opacityPercent = 100;
    private bool _contextMenuEnabled = true;
    private readonly Border _frameBorder;
    private readonly System.Windows.Controls.ToolTip _titleToolTip;
    private ThumbnailSizeMode _sizeMode = ThumbnailSizeMode.Adaptive;
    private UniformContentMode _uniformContent = UniformContentMode.Crop;

    internal ThumbnailWindow(nint source, string title, string executablePath, nint taskViewOwner)
    {
        _source = source;
        _sourceThreadId = NativeMethods.GetWindowThreadProcessId(source, out _sourceProcessId);
        _taskViewOwner = taskViewOwner;
        _executablePath = executablePath;
        Title = title;
        _titleToolTip = new System.Windows.Controls.ToolTip
        {
            Content = new TextBlock
            {
                Text = title,
                MaxWidth = 420,
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };
        ToolTipService.SetInitialShowDelay(this, 450);
        ToolTipService.SetShowDuration(this, 10000);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = false;
        Background = System.Windows.Media.Brushes.Transparent;
        _frameBorder = new Border
        {
            Background = System.Windows.Media.Brushes.Transparent,
            BorderBrush = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            IsHitTestVisible = false
        };
        Content = _frameBorder;
        Topmost = false;
        ShowActivated = false;
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => Unregister();
        Deactivated += (_, _) => SendToBottom();
        // Keep restoration exclusively in these mouse handlers. Restoring from
        // WM_ACTIVATE bypasses RestoreOnSingleClick because an ordinary click can
        // activate the thumbnail before WPF has evaluated the configured click mode.
        MouseLeftButtonUp += (_, _) => { if (_restoreOnSingleClick) Restore(); };
        MouseDoubleClick += (_, e) => { if (!_restoreOnSingleClick && e.ChangedButton == MouseButton.Left) Restore(); };
        MouseRightButtonUp += (_, e) =>
        {
            if (!_contextMenuEnabled) return;
            e.Handled = true;
            ShowSourceSystemMenu();
        };
    }

    internal bool HasRegisteredThumbnail => _thumbnail != 0;

    internal bool MatchesSource(nint source)
    {
        if (source != _source || _thumbnail == 0) return false;
        var threadId = NativeMethods.GetWindowThreadProcessId(source, out var processId);
        return threadId != 0 && threadId == _sourceThreadId && processId == _sourceProcessId &&
               NativeMethods.DwmQueryThumbnailSourceSize(_thumbnail, out _) == 0;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        var hwndSource = HwndSource.FromHwnd(_handle);
        if (hwndSource?.CompositionTarget is not null)
            hwndSource.CompositionTarget.BackgroundColor = System.Windows.Media.Color.FromArgb(0, 0, 0, 0);
        ApplyTaskViewMode();
        var glass = new NativeMethods.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        NativeMethods.DwmExtendFrameIntoClientArea(_handle, ref glass);
        _iconBadge = new IconBadgeWindow(this, _executablePath);
        if (NativeMethods.DwmRegisterThumbnail(_handle, _source, out _thumbnail) == 0) UpdateThumbnail();
        SendToBottom();
    }

    internal void ApplyBounds(int x, int y, int width, int height)
    {
        _pixelX = x;
        _pixelY = y;
        _pixelWidth = width;
        _pixelHeight = height;
        var scale = _handle == 0 ? 1d : Math.Max(1d, NativeMethods.GetDpiForWindow(_handle) / 96d);
        Left = x / scale; Top = y / scale; Width = width / scale; Height = height / scale;
        UpdateThumbnail();
        SendToBottom();
        _iconBadge?.ApplyPosition(x, y, width, height, _iconPosition, _showIcon);
    }

    internal void SetIconVisibility(bool visible)
    {
        _showIcon = visible;
        _iconBadge?.ApplyPosition(_pixelX, _pixelY, _pixelWidth, _pixelHeight, _iconPosition, visible);
    }

    internal void SetRestoreOnSingleClick(bool singleClick) => _restoreOnSingleClick = singleClick;

    internal void SetSizingMode(ThumbnailSizeMode sizeMode, UniformContentMode uniformContent)
    {
        _sizeMode = sizeMode;
        _uniformContent = uniformContent;
        UpdateThumbnail();
    }

    internal void SetIconPosition(ThumbnailIconPosition position)
    {
        _iconPosition = position;
        _iconBadge?.ApplyPosition(_pixelX, _pixelY, _pixelWidth, _pixelHeight, position, _showIcon);
    }

    internal void SetContextMenuEnabled(bool enabled) => _contextMenuEnabled = enabled;

    internal void SetTitleTooltipEnabled(bool enabled) => ToolTip = enabled ? _titleToolTip : null;

    private void ApplyTaskViewMode()
    {
        if (_handle == 0) return;
        var extendedStyle = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GWL_EXSTYLE);
        extendedStyle = new nint((extendedStyle.ToInt64() | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE) & ~NativeMethods.WS_EX_APPWINDOW);
        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GWL_EXSTYLE, extendedStyle);
        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GWLP_HWNDPARENT, _taskViewOwner);
        NativeMethods.SetWindowPos(_handle, 0, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER |
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
    }

    internal void SetThumbnailOpacity(int opacityPercent)
    {
        _opacityPercent = Math.Clamp(opacityPercent, 20, 100);
        UpdateThumbnail();
        _iconBadge?.SetOpacity(_opacityPercent);
    }

    internal void SetFrameStyle(ThumbnailFrameStyle frameStyle)
    {
        _frameStyle = frameStyle;
        Background = System.Windows.Media.Brushes.Transparent;
        _frameBorder.BorderThickness = frameStyle == ThumbnailFrameStyle.None ? new Thickness(0) : new Thickness(4);
        _frameBorder.BorderBrush = frameStyle == ThumbnailFrameStyle.None
            ? System.Windows.Media.Brushes.Transparent
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(82, 82, 82));
        _frameBorder.CornerRadius = frameStyle == ThumbnailFrameStyle.Rounded ? new CornerRadius(9) : new CornerRadius(0);
        if (_handle != 0)
        {
            const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
            var preference = frameStyle == ThumbnailFrameStyle.Rounded ? 2 : 1;
            NativeMethods.DwmSetWindowAttribute(_handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
        UpdateThumbnail();
    }

    internal void UpdateThumbnail()
    {
        if (_thumbnail == 0 || _pixelWidth <= 0 || _pixelHeight <= 0) return;
        var inset = _frameStyle == ThumbnailFrameStyle.None ? 0 : 4;
        var innerWidth = Math.Max(1, _pixelWidth - inset * 2);
        var innerHeight = Math.Max(1, _pixelHeight - inset * 2);
        var destination = new NativeMethods.Rect { Left = inset, Top = inset, Right = inset + innerWidth, Bottom = inset + innerHeight };
        var sourceRect = new NativeMethods.Rect();
        var flags = NativeMethods.DWM_TNP_RECTDESTINATION | NativeMethods.DWM_TNP_VISIBLE | NativeMethods.DWM_TNP_SOURCECLIENTAREAONLY | NativeMethods.DWM_TNP_OPACITY;

        if (_sizeMode == ThumbnailSizeMode.Uniform &&
            NativeMethods.DwmQueryThumbnailSourceSize(_thumbnail, out var sourceSize) == 0 &&
            sourceSize.Width > 0 && sourceSize.Height > 0)
        {
            var sourceAspect = (double)sourceSize.Width / sourceSize.Height;
            var targetAspect = (double)innerWidth / innerHeight;
            if (_uniformContent == UniformContentMode.Crop)
            {
                int cropWidth, cropHeight;
                if (sourceAspect > targetAspect)
                {
                    cropHeight = sourceSize.Height;
                    cropWidth = Math.Max(1, (int)Math.Round(cropHeight * targetAspect));
                }
                else
                {
                    cropWidth = sourceSize.Width;
                    cropHeight = Math.Max(1, (int)Math.Round(cropWidth / targetAspect));
                }
                var sourceLeft = (sourceSize.Width - cropWidth) / 2;
                var sourceTop = (sourceSize.Height - cropHeight) / 2;
                sourceRect = new NativeMethods.Rect { Left = sourceLeft, Top = sourceTop, Right = sourceLeft + cropWidth, Bottom = sourceTop + cropHeight };
                flags |= NativeMethods.DWM_TNP_RECTSOURCE;
            }
            else
            {
                var scale = Math.Min((double)innerWidth / sourceSize.Width, (double)innerHeight / sourceSize.Height);
                var contentWidth = Math.Max(1, (int)Math.Round(sourceSize.Width * scale));
                var contentHeight = Math.Max(1, (int)Math.Round(sourceSize.Height * scale));
                var left = inset + (innerWidth - contentWidth) / 2;
                var top = inset + (innerHeight - contentHeight) / 2;
                destination = new NativeMethods.Rect { Left = left, Top = top, Right = left + contentWidth, Bottom = top + contentHeight };
            }
        }
        var props = new NativeMethods.DwmThumbnailProperties
        {
            Flags = flags,
            Destination = destination,
            Source = sourceRect,
            Visible = true,
            SourceClientAreaOnly = false,
            Opacity = (byte)Math.Round(255 * _opacityPercent / 100d)
        };
        NativeMethods.DwmUpdateThumbnailProperties(_thumbnail, ref props);
    }

    internal (int Width, int Height) GetPreferredSize(int maximumWidth, int maximumHeight, ThumbnailSizeMode sizeMode)
    {
        if (sizeMode == ThumbnailSizeMode.Uniform) return (maximumWidth, maximumHeight);
        if (_thumbnail == 0 || NativeMethods.DwmQueryThumbnailSourceSize(_thumbnail, out var source) != 0 || source.Width <= 0 || source.Height <= 0)
            return (maximumWidth, maximumHeight);
        var scale = Math.Min((double)maximumWidth / source.Width, (double)maximumHeight / source.Height);
        return (Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)));
    }

    internal void SendToBottom()
    {
        if (_handle != 0) NativeMethods.SetWindowPos(_handle, NativeMethods.HWND_BOTTOM, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void Restore()
    {
        if (NativeMethods.IsWindow(_source))
        {
            // OpenIcon is specifically intended for iconic (minimized) windows and
            // handles system windows such as Task Manager more reliably.
            NativeMethods.OpenIcon(_source);
            NativeMethods.ShowWindowAsync(_source, NativeMethods.SW_RESTORE);
            NativeMethods.BringWindowToTop(_source);
            NativeMethods.SetForegroundWindow(_source);
            if (NativeMethods.IsIconic(_source))
                NativeMethods.SwitchToThisWindow(_source, true);
        }
    }

    private void ShowSourceSystemMenu()
    {
        if (!NativeMethods.IsWindow(_source) || _handle == 0) return;
        var menu = NativeMethods.GetSystemMenu(_source, false);
        if (menu == 0 || !NativeMethods.GetCursorPos(out var point)) return;
        NativeMethods.SetForegroundWindow(_handle);
        var command = NativeMethods.TrackPopupMenuEx(menu, NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_RIGHTBUTTON, point.X, point.Y, _handle, 0);
        if (command == 0) return;
        if ((command & 0xFFF0) == NativeMethods.SC_RESTORE) Restore();
        else NativeMethods.PostMessage(_source, NativeMethods.WM_SYSCOMMAND, new nint(command), 0);
        SendToBottom();
    }

    private void Unregister()
    {
        if (_thumbnail != 0) { NativeMethods.DwmUnregisterThumbnail(_thumbnail); _thumbnail = 0; }
    }
}
