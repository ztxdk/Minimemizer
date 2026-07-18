using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Effects;
using System.Text;
using Button = System.Windows.Controls.Button;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Path = System.IO.Path;
using Control = System.Windows.Controls.Control;
using Cursors = System.Windows.Input.Cursors;
using TextElement = System.Windows.Documents.TextElement;

namespace Minimemizer;

internal sealed class ThumbnailWindow : Window
{
    private const int TitleBarHeightPixels = 32;
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
    private int _effectiveTitleBarHeightPixels = TitleBarHeightPixels;
    private int _lastPreferredHeight;
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
    private readonly TextBlock _titleToolTipText;
    private readonly TextBlock _insideTitleText;
    private readonly TextBlock _aboveTitleText;
    private readonly Border _insideTitleBorder;
    private readonly Border _aboveTitleBorder;
    private SolidColorBrush _menuButtonNormal = Brushes.Transparent;
    private SolidColorBrush _menuButtonHover = Brushes.Transparent;
    private ThumbnailSizeMode _sizeMode = ThumbnailSizeMode.Adaptive;
    private UniformContentMode _uniformContent = UniformContentMode.Crop;
    private NativeMethods.Point _dragStartCursor;
    private int _dragOffsetX;
    private int _dragOffsetY;
    private bool _dragArmed;
    private bool _isDragging;
    private ThumbnailTitleMode _titleMode = ThumbnailTitleMode.Hover;
    private bool? _appliedDarkTheme;

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
        _titleToolTipText = new TextBlock
        {
            Text = title,
            MaxWidth = 420,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 13
        };
        _titleToolTip = new System.Windows.Controls.ToolTip
        {
            Content = _titleToolTipText,
            Placement = PlacementMode.Mouse,
            HorizontalOffset = 12,
            VerticalOffset = 16,
            Padding = new Thickness(11, 7, 11, 7),
            BorderThickness = new Thickness(1),
            HasDropShadow = false,
            Template = CreateRoundedToolTipTemplate()
        };
        _titleToolTip.Opened += (_, _) => ApplyHoverTheme();
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
        var shortenedTitle = WindowTitleFormatter.Shorten(title, executablePath);
        _insideTitleText = TitleText(shortenedTitle);
        _aboveTitleText = TitleText(shortenedTitle);
        _insideTitleBorder = new Border
        {
            Child = _insideTitleText,
            Padding = new Thickness(9, 5, 9, 5),
            Margin = new Thickness(8),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        _aboveTitleBorder = new Border
        {
            Child = _aboveTitleText,
            Padding = new Thickness(10, 0, 10, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            BorderThickness = new Thickness(1, 1, 1, 0),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        _zoneMenuButton = new Button
        {
            Content = "⋯",
            Width = 36,
            Height = 36,
            Margin = new Thickness(8),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Visibility = Visibility.Collapsed,
            Cursor = Cursors.Hand,
            Template = CreateRoundedButtonTemplate(),
            Effect = new DropShadowEffect { BlurRadius = 9, ShadowDepth = 2, Opacity = .38 }
        };
        ApplyHoverTheme();
        _zoneMenuButton.MouseEnter += (_, _) => _zoneMenuButton.Background = _menuButtonHover;
        _zoneMenuButton.MouseLeave += (_, _) => _zoneMenuButton.Background = _menuButtonNormal;
        _zoneMenuButton.Click += (_, e) =>
        {
            e.Handled = true;
            ZoneMenuRequested?.Invoke(this, _zoneMenuButton);
        };
        var root = new Grid { Background = Brushes.Transparent };
        root.Children.Add(_frameBorder);
        root.Children.Add(_insideTitleBorder);
        root.Children.Add(_aboveTitleBorder);
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
        MouseEnter += (_, _) =>
        {
            ApplyHoverTheme();
            _zoneMenuButton.Visibility = Visibility.Visible;
        };
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
        ApplyTitleLayout();
        SendToBottom();
    }

    internal void ApplyBounds(int x, int y, int width, int height)
    {
        if (_titleMode == ThumbnailTitleMode.AlwaysAbove)
        {
            var scaled = _lastPreferredHeight > 0
                ? (int)Math.Round(TitleBarHeightPixels * (double)height / _lastPreferredHeight)
                : TitleBarHeightPixels;
            _effectiveTitleBarHeightPixels = Math.Min(Math.Max(1, height - 1), Math.Max(20, scaled));
        }
        else _effectiveTitleBarHeightPixels = 0;
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
        ApplyTitleLayout();
        ApplyIconPosition(_showIcon);
    }

    private void ApplyHoverTheme()
    {
        var dark = AppTheme.IsDarkModeEnabled();
        _appliedDarkTheme = dark;
        _menuButtonNormal = Solid(dark ? Color.FromArgb(242, 38, 38, 38) : Color.FromArgb(245, 249, 249, 249));
        _menuButtonHover = Solid(dark ? Color.FromArgb(250, 61, 61, 61) : Color.FromArgb(252, 232, 232, 232));
        _zoneMenuButton.Background = _zoneMenuButton.IsMouseOver ? _menuButtonHover : _menuButtonNormal;
        _zoneMenuButton.Foreground = Solid(dark ? Color.FromRgb(245, 245, 245) : Color.FromRgb(28, 28, 28));
        _zoneMenuButton.BorderBrush = Solid(dark ? Color.FromRgb(91, 91, 91) : Color.FromRgb(188, 188, 188));
        _titleToolTip.Background = Solid(dark ? Color.FromRgb(38, 38, 38) : Color.FromRgb(249, 249, 249));
        _titleToolTip.Foreground = Solid(dark ? Color.FromRgb(242, 242, 242) : Color.FromRgb(24, 24, 24));
        _titleToolTip.BorderBrush = Solid(dark ? Color.FromRgb(80, 80, 80) : Color.FromRgb(200, 200, 200));
        _insideTitleBorder.Background = Solid(dark ? Color.FromArgb(226, 34, 34, 34) : Color.FromArgb(232, 250, 250, 250));
        _insideTitleBorder.BorderBrush = Solid(dark ? Color.FromRgb(83, 83, 83) : Color.FromRgb(195, 195, 195));
        _insideTitleBorder.BorderThickness = new Thickness(1);
        _aboveTitleBorder.Background = Solid(dark ? Color.FromRgb(38, 38, 38) : Color.FromRgb(249, 249, 249));
        _aboveTitleBorder.BorderBrush = Solid(dark ? Color.FromRgb(82, 82, 82) : Color.FromRgb(195, 195, 195));
        var titleForeground = Solid(dark ? Color.FromRgb(242, 242, 242) : Color.FromRgb(24, 24, 24));
        _insideTitleText.Foreground = titleForeground;
        _aboveTitleText.Foreground = titleForeground;
    }

    private static ControlTemplate CreateRoundedButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    private static ControlTemplate CreateRoundedToolTipTemplate()
    {
        var template = new ControlTemplate(typeof(System.Windows.Controls.ToolTip));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.EffectProperty, new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = .3 });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(TextElement.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    private static SolidColorBrush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static TextBlock TitleText(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        VerticalAlignment = VerticalAlignment.Center
    };

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
        ApplyTitleLayout();
        ApplyIconPosition(visible);
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
        ApplyTitleLayout();
        ApplyIconPosition(_showIcon);
    }

    internal void SetContextMenuEnabled(bool enabled) => _contextMenuEnabled = enabled;

    internal void SetTitleMode(ThumbnailTitleMode mode)
    {
        _titleMode = mode;
        ToolTip = mode == ThumbnailTitleMode.Hover ? _titleToolTip : null;
        _insideTitleBorder.Visibility = mode == ThumbnailTitleMode.AlwaysInside ? Visibility.Visible : Visibility.Collapsed;
        _aboveTitleBorder.Visibility = mode == ThumbnailTitleMode.AlwaysAbove ? Visibility.Visible : Visibility.Collapsed;
        ApplyTitleLayout();
        ApplyFrameAppearance();
        UpdateThumbnail();
        ApplyIconPosition(_showIcon);
    }

    internal void RefreshTitle()
    {
        if (!NativeMethods.IsWindow(_source)) return;
        if (_appliedDarkTheme != AppTheme.IsDarkModeEnabled()) ApplyHoverTheme();
        var buffer = new StringBuilder(1024);
        NativeMethods.GetWindowText(_source, buffer, buffer.Capacity);
        var fullTitle = buffer.ToString().Trim();
        if (string.Equals(Title, fullTitle, StringComparison.Ordinal)) return;
        Title = fullTitle;
        _titleToolTipText.Text = string.IsNullOrWhiteSpace(fullTitle)
            ? WindowTitleFormatter.GetProgramDisplayName(_executablePath)
            : fullTitle;
        var shortened = WindowTitleFormatter.Shorten(fullTitle, _executablePath);
        _insideTitleText.Text = shortened;
        _aboveTitleText.Text = shortened;
    }

    private void ApplyIconPosition(bool visible)
    {
        var previewTop = _titleMode == ThumbnailTitleMode.AlwaysAbove ? _effectiveTitleBarHeightPixels : 0;
        _iconBadge?.ApplyPosition(_pixelX, _pixelY + previewTop, _pixelWidth,
            Math.Max(1, _pixelHeight - previewTop), _iconPosition, visible);
    }

    private void ApplyTitleLayout()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var topPixels = _titleMode == ThumbnailTitleMode.AlwaysAbove ? _effectiveTitleBarHeightPixels : 0;
        var topDip = topPixels / Math.Max(0.1, dpi.DpiScaleY);
        _aboveTitleBorder.Height = topPixels / Math.Max(0.1, dpi.DpiScaleY);
        _frameBorder.Margin = new Thickness(0, topDip, 0, 0);
        _zoneMenuButton.Margin = new Thickness(8, topDip + 8, 8, 8);
        _insideTitleBorder.HorizontalAlignment = _showIcon &&
            _iconPosition is ThumbnailIconPosition.BottomLeft or ThumbnailIconPosition.BottomCenter
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left;
        var availableDipWidth = Math.Max(40, _pixelWidth / Math.Max(0.1, dpi.DpiScaleX) - 16);
        _insideTitleBorder.MaxWidth = availableDipWidth;
        _aboveTitleText.MaxWidth = Math.Max(20, availableDipWidth - 4);
    }

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
        ApplyFrameAppearance();
        UpdateThumbnail();
    }

    private void ApplyFrameAppearance()
    {
        Background = System.Windows.Media.Brushes.Transparent;
        _frameBorder.BorderThickness = _frameStyle == ThumbnailFrameStyle.None ? new Thickness(0) : new Thickness(4);
        _frameBorder.BorderBrush = _frameStyle == ThumbnailFrameStyle.None
            ? System.Windows.Media.Brushes.Transparent
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(82, 82, 82));
        _frameBorder.CornerRadius = _frameStyle == ThumbnailFrameStyle.Rounded
            ? (_titleMode == ThumbnailTitleMode.AlwaysAbove ? new CornerRadius(0, 0, 9, 9) : new CornerRadius(9))
            : new CornerRadius(0);
        _aboveTitleBorder.CornerRadius = _frameStyle == ThumbnailFrameStyle.Rounded
            ? new CornerRadius(9, 9, 0, 0)
            : new CornerRadius(0);
        if (_handle != 0)
        {
            const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
            var preference = _frameStyle == ThumbnailFrameStyle.Rounded ? 2 : 1;
            NativeMethods.DwmSetWindowAttribute(_handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
    }

    internal void UpdateThumbnail()
    {
        if (_thumbnail == 0 || _pixelWidth <= 0 || _pixelHeight <= 0) return;
        var inset = _frameStyle == ThumbnailFrameStyle.None ? 0 : 4;
        var previewTop = _titleMode == ThumbnailTitleMode.AlwaysAbove ? _effectiveTitleBarHeightPixels : 0;
        var innerWidth = Math.Max(1, _pixelWidth - inset * 2);
        var innerHeight = Math.Max(1, _pixelHeight - previewTop - inset * 2);
        var destination = new NativeMethods.Rect { Left = inset, Top = previewTop + inset, Right = inset + innerWidth, Bottom = previewTop + inset + innerHeight };
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
                var top = previewTop + inset + (innerHeight - contentHeight) / 2;
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
        var titleHeight = _titleMode == ThumbnailTitleMode.AlwaysAbove ? TitleBarHeightPixels : 0;
        if (sizeMode == ThumbnailSizeMode.Uniform)
        {
            _lastPreferredHeight = maximumHeight + titleHeight;
            return (maximumWidth, _lastPreferredHeight);
        }
        if (_thumbnail == 0 || NativeMethods.DwmQueryThumbnailSourceSize(_thumbnail, out var source) != 0 || source.Width <= 0 || source.Height <= 0)
        {
            _lastPreferredHeight = maximumHeight + titleHeight;
            return (maximumWidth, _lastPreferredHeight);
        }
        var scale = Math.Min((double)maximumWidth / source.Width, (double)maximumHeight / source.Height);
        _lastPreferredHeight = Math.Max(1, (int)Math.Round(source.Height * scale)) + titleHeight;
        return (Math.Max(1, (int)Math.Round(source.Width * scale)), _lastPreferredHeight);
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
