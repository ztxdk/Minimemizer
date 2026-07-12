using System.Text;
using System.Windows;
using Forms = System.Windows.Forms;
using Application = System.Windows.Application;
using System.Windows.Threading;

namespace Minimemizer;

public sealed class WindowManager : IDisposable
{
    private readonly SettingsStore _store;
    private readonly Dictionary<nint, ThumbnailWindow> _thumbnails = [];
    private readonly NativeMethods.WinEventDelegate _callback;
    private nint _minimizeHook;
    private nint _destroyHook;
    private readonly DispatcherTimer _scanTimer;
    private bool _disposed;

    public WindowManager(SettingsStore store)
    {
        _store = store;
        _callback = OnWinEvent;
        _scanTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _scanTimer.Tick += (_, _) => ScanWindows();
    }

    public void Start()
    {
        var flags = NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS;
        _minimizeHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_MINIMIZESTART, NativeMethods.EVENT_SYSTEM_MINIMIZEEND, 0, _callback, 0, 0, flags);
        _destroyHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_DESTROY, 0, _callback, 0, 0, flags);
        NativeMethods.EnumWindows((hwnd, _) => { if (NativeMethods.IsIconic(hwnd)) Add(hwnd); return true; }, 0);
        _scanTimer.Start();
    }

    private void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (hwnd == 0 || _disposed) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (eventType == NativeMethods.EVENT_SYSTEM_MINIMIZESTART) Add(hwnd);
            else if (eventType == NativeMethods.EVENT_SYSTEM_MINIMIZEEND || eventType == NativeMethods.EVENT_OBJECT_DESTROY) Remove(hwnd);
        });
    }

    private void Add(nint hwnd)
    {
        if (_thumbnails.ContainsKey(hwnd) || !IsEligible(hwnd)) return;
        var titleBuffer = new StringBuilder(512);
        NativeMethods.GetWindowText(hwnd, titleBuffer, titleBuffer.Capacity);
        var window = new ThumbnailWindow(hwnd, titleBuffer.ToString(), NativeMethods.GetProcessPath(hwnd));
        _thumbnails.Add(hwnd, window);
        window.Show();
        Relayout();
    }

    private bool IsEligible(nint hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd)) return false;
        var owner = NativeMethods.GetWindow(hwnd, 4); // GW_OWNER
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        if ((style & NativeMethods.WS_EX_TOOLWINDOW) != 0) return false;
        if (owner != 0 && (style & NativeMethods.WS_EX_APPWINDOW) == 0) return false;
        var title = new StringBuilder(2);
        if (NativeMethods.GetWindowText(hwnd, title, 2) == 0) return false;
        var path = NativeMethods.GetProcessPath(hwnd);
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return false;
        return !_store.Current.ExcludedPaths.Contains(path, StringComparer.OrdinalIgnoreCase);
    }

    private void Remove(nint hwnd)
    {
        if (!_thumbnails.Remove(hwnd, out var window)) return;
        window.Close();
        Relayout();
    }

    public void Refresh()
    {
        ScanWindows();
        Relayout();
    }

    public void PreviewOpacity(int opacityPercent)
    {
        if (_disposed) return;
        foreach (var window in _thumbnails.Values)
            window.SetThumbnailOpacity(opacityPercent);
    }

    private void ScanWindows()
    {
        foreach (var hwnd in _thumbnails.Keys.ToArray())
            if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.IsIconic(hwnd) || !IsEligible(hwnd)) Remove(hwnd);
        NativeMethods.EnumWindows((hwnd, _) => { if (NativeMethods.IsIconic(hwnd)) Add(hwnd); return true; }, 0);
    }

    public void Relayout()
    {
        if (_thumbnails.Count == 0) return;
        var settings = _store.Current;
        var screens = Forms.Screen.AllScreens;
        var screen = screens.FirstOrDefault(s => string.Equals(s.DeviceName, settings.ScreenDeviceName, StringComparison.OrdinalIgnoreCase))
                     ?? screens.FirstOrDefault(s => s.Primary) ?? screens[0];
        var area = screen.WorkingArea;
        var items = _thumbnails.Values
            .Select(window => (Window: window, Size: window.GetPreferredSize(settings.ThumbnailWidth, settings.ThumbnailHeight, settings.SizeMode)))
            .ToArray();
        var vertical = settings.Flow == ThumbnailFlow.Vertical;
        var availableLength = (vertical ? area.Height : area.Width) - 2 * settings.EdgeMargin;
        var naturalLength = items.Sum(item => vertical ? item.Size.Height : item.Size.Width) + settings.Gap * Math.Max(0, items.Length - 1);
        var scale = naturalLength > 0 ? Math.Min(1d, Math.Max(0.05d, (double)availableLength / naturalLength)) : 1d;
        var gap = (int)Math.Round(settings.Gap * scale);
        var fromRight = settings.Corner is ScreenCorner.TopRight or ScreenCorner.BottomRight;
        var fromBottom = settings.Corner is ScreenCorner.BottomLeft or ScreenCorner.BottomRight;
        var cursor = vertical
            ? (fromBottom ? area.Bottom - settings.EdgeMargin : area.Top + settings.EdgeMargin)
            : (fromRight ? area.Right - settings.EdgeMargin : area.Left + settings.EdgeMargin);

        foreach (var item in items)
        {
            var width = Math.Max(1, (int)Math.Round(item.Size.Width * scale));
            var height = Math.Max(1, (int)Math.Round(item.Size.Height * scale));
            int x, y;
            if (vertical)
            {
                y = fromBottom ? cursor - height : cursor;
                x = fromRight ? area.Right - settings.EdgeMargin - width : area.Left + settings.EdgeMargin;
                cursor += fromBottom ? -(height + gap) : height + gap;
            }
            else
            {
                x = fromRight ? cursor - width : cursor;
                y = fromBottom ? area.Bottom - settings.EdgeMargin - height : area.Top + settings.EdgeMargin;
                cursor += fromRight ? -(width + gap) : width + gap;
            }
            item.Window.SetFrameStyle(settings.FrameStyle);
            item.Window.SetSizingMode(settings.SizeMode, settings.UniformContent);
            item.Window.SetIconPosition(settings.IconPosition);
            item.Window.SetThumbnailOpacity(settings.ThumbnailOpacity);
            item.Window.SetContextMenuEnabled(settings.EnableThumbnailContextMenu);
            item.Window.ApplyBounds(x, y, width, height);
            item.Window.SetIconVisibility(settings.ShowProgramIcon);
            item.Window.SetRestoreOnSingleClick(settings.RestoreOnSingleClick);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scanTimer.Stop();
        if (_minimizeHook != 0) NativeMethods.UnhookWinEvent(_minimizeHook);
        if (_destroyHook != 0) NativeMethods.UnhookWinEvent(_destroyHook);
        foreach (var window in _thumbnails.Values.ToArray()) window.Close();
        _thumbnails.Clear();
    }
}
