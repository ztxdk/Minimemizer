using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Path = System.IO.Path;

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
    private readonly Button _zoneMenuButton;
    private readonly System.Windows.Controls.ToolTip _titleToolTip;
    private ThumbnailSizeMode _sizeMode = ThumbnailSizeMode.Adaptive;
    private UniformContentMode _uniformContent = UniformContentMode.Crop;
    private NativeMethods.Point _dragStartCursor;
    private int _dragOffsetX;
    private int _dragOffsetY;
    private bool _dragArmed;
    private bool _isDragging;

    internal event Action<ThumbnailWindow, NativeMethods.Point>? DragStarted;
    internal event Action<ThumbnailWindow, NativeMethods.Point>? DragMoved;
    internal event Action<ThumbnailWindow, NativeMethods.Point>? DragCompleted;
    internal event Action<ThumbnailWindow, Button>? ZoneMenuRequested;

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
        _zoneMenuButton = new Button
        {
            Content = "⋯",
            Width = 32,
            Height = 28,
            Margin = new Thickness(7),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(220, 32, 32, 32)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Visibility = Visibility.Collapsed,
            ToolTip = "Minimemizer"
        };
        _zoneMenuButton.Click += (_, e) =>
        {
            e.Handled = true;
            ZoneMenuRequested?.Invoke(this, _zoneMenuButton);
        };
        var root = new Grid { Background = Brushes.Transparent };
        root.Children.Add(_frameBorder);
        root.Children.Add(_zoneMenuButton);
        Content = root;
        Topmost = false;
        ShowActivated = false;
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => Unregister();
        Deactivated += (_, _) => SendToBottom();
        // Keep restoration exclusively in these mouse handlers. Restoring from
        // WM_ACTIVATE bypasses RestoreOnSingleClick because an ordinary click can
        // activate the thumbnail before WPF has evaluated the configured click mode.
        MouseEnter += (_, _) => _zoneMenuButton.Visibility = Visibility.Visible;
        MouseLeave += (_, _) => { if (!_zoneMenuButton.IsMouseOver) _zoneMenuButton.Visibility = Visibility.Collapsed; };
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        LostMouseCapture += OnLostMouseCapture;
        MouseDoubleClick += (_, e) => { if (!_isDragging && !_restoreOnSingleClick && e.ChangedButton == MouseButton.Left) Restore(); };
        MouseRightButtonUp += (_, e) =>
        {
            if (!_contextMenuEnabled) return;
            e.Handled = true;
            ShowSourceSystemMenu();
        };
    }

    internal bool HasRegisteredThumbnail => _thumbnail != 0;
    internal nint SourceHandle => _source;
    internal uint SourceProcessId => _sourceProcessId;
    internal string ExecutablePath => _executablePath;
    internal string ProgramName => Path.GetFileNameWithoutExtension(_executablePath);

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
        if (_handle != 0)
            NativeMethods.SetWindowPos(_handle, NativeMethods.HWND_BOTTOM, x, y, width, height, NativeMethods.SWP_NOACTIVATE);
        else
        {
            Left = x;
            Top = y;
            Width = width;
            Height = height;
        }
        UpdateThumbnail();
        _iconBadge?.ApplyPosition(x, y, width, height, _iconPosition, _showIcon);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !NativeMethods.GetCursorPos(out _dragStartCursor)) return;
        _dragOffsetX = _dragStartCursor.X - _pixelX;
        _dragOffsetY = _dragStartCursor.Y - _pixelY;
        _dragArmed = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed || !NativeMethods.GetCursorPos(out var cursor)) return;
        if (!_isDragging)
        {
            var horizontal = Math.Abs(cursor.X - _dragStartCursor.X);
            var vertical = Math.Abs(cursor.Y - _dragStartCursor.Y);
            if (horizontal < SystemParameters.MinimumHorizontalDragDistance && vertical < SystemParameters.MinimumVerticalDragDistance) return;
            _isDragging = true;
            CaptureMouse();
            _zoneMenuButton.Visibility = Visibility.Collapsed;
            DragStarted?.Invoke(this, cursor);
        }

        ApplyBounds(cursor.X - _dragOffsetX, cursor.Y - _dragOffsetY, _pixelWidth, _pixelHeight);
        DragMoved?.Invoke(this, cursor);
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _dragArmed = false;
        if (_isDragging)
        {
            _isDragging = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            if (NativeMethods.GetCursorPos(out var cursor)) DragCompleted?.Invoke(this, cursor);
            e.Handled = true;
            return;
        }
        if (_restoreOnSingleClick) Restore();
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        _dragArmed = false;
        if (NativeMethods.GetCursorPos(out var cursor)) DragCompleted?.Invoke(this, cursor);
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
