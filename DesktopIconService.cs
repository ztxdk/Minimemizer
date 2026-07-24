using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Minimemizer;

internal sealed class DesktopIconService : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMouseMove = 0x0200;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmLeftButtonUp = 0x0202;
    private const int ToggleDesktopIconsCommand = 0x7402;

    private readonly SettingsStore _store;
    private readonly WindowManager _manager;
    private readonly Dispatcher _dispatcher;
    private readonly Action<string> _showError;
    private readonly NativeMethods.LowLevelMouseProc _mouseCallback;
    private readonly BlockingCollection<DesktopClick> _clicks = new();
    private readonly Thread _hitTestThread;
    private readonly DispatcherTimer _stateTimer;
    private nint _mouseHook;
    private NativeMethods.Point _mouseDownPoint;
    private bool _leftButtonDown;
    private bool _dragged;
    private bool _modifierThumbnailFollow;
    private bool _disposed;

    internal DesktopIconService(SettingsStore store, WindowManager manager, Action<string> showError)
    {
        _store = store;
        _manager = manager;
        _dispatcher = System.Windows.Application.Current.Dispatcher;
        _showError = showError;
        _mouseCallback = MouseHook;
        _hitTestThread = new Thread(ProcessDesktopClicks)
        {
            IsBackground = true,
            Name = "Minimemizer desktop hit testing"
        };
        _hitTestThread.SetApartmentState(ApartmentState.MTA);
        _hitTestThread.Start();
        _stateTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _stateTimer.Tick += (_, _) => SynchronizeThumbnailVisibility();
    }

    internal bool IconsVisible => !TryGetIconsVisible(out var visible) || visible;

    internal void Start() => ApplySettings();

    internal void ApplySettings()
    {
        if (_disposed) return;
        _modifierThumbnailFollow = false;
        if (_store.Current.ToggleDesktopIconsOnEmptyDoubleClick) InstallMouseHook();
        else ReleaseMouseHook();

        if (_store.Current.HideThumbnailsWithDesktopIcons)
            SynchronizeThumbnailVisibility();
        else
            _manager.SetThumbnailsVisible(true);
        UpdateStateMonitoring();
    }

    internal void ToggleIcons() => ToggleIcons(includeThumbnailsForThisToggle: false);

    private void ToggleIcons(bool includeThumbnailsForThisToggle)
    {
        if (_disposed) return;
        if (!TryGetDesktopView(out var view, out _) || !TryGetIconsVisible(out var visible))
        {
            _showError(Localizer.T(_store.Current.Language, "Windows-skrivebordet kunne ikke findes."));
            return;
        }

        NativeMethods.SendMessage(view, NativeMethods.WM_COMMAND, ToggleDesktopIconsCommand, 0);
        if (!TryGetIconsVisible(out var updated) || updated == visible)
        {
            _showError(Localizer.T(_store.Current.Language, "Skrivebordsikonerne kunne ikke ændres."));
            return;
        }
        _modifierThumbnailFollow = !updated &&
                                   includeThumbnailsForThisToggle &&
                                   !_store.Current.HideThumbnailsWithDesktopIcons;
        SynchronizeThumbnailVisibility(updated);
        UpdateStateMonitoring();
    }

    private void InstallMouseHook()
    {
        if (_mouseHook != 0) return;
        _mouseHook = NativeMethods.SetWindowsHookEx(WhMouseLl, _mouseCallback, NativeMethods.GetModuleHandle(null), 0);
        if (_mouseHook == 0)
            _showError(Localizer.T(_store.Current.Language, "Dobbeltklik på skrivebordet kunne ikke aktiveres."));
    }

    private nint MouseHook(int code, nint message, nint data)
    {
        if (code >= 0 && data != 0)
        {
            var value = Marshal.PtrToStructure<MouseHookData>(data);
            switch (message.ToInt32())
            {
                case WmLeftButtonDown:
                    _mouseDownPoint = value.Point;
                    _leftButtonDown = true;
                    _dragged = false;
                    break;
                case WmMouseMove when _leftButtonDown:
                    var dragSize = Forms.SystemInformation.DragSize;
                    if (Math.Abs(value.Point.X - _mouseDownPoint.X) >= Math.Max(1, dragSize.Width / 2) ||
                        Math.Abs(value.Point.Y - _mouseDownPoint.Y) >= Math.Max(1, dragSize.Height / 2))
                        _dragged = true;
                    break;
                case WmLeftButtonUp:
                    if (_leftButtonDown && !_dragged && !_clicks.IsAddingCompleted)
                    {
                        try { _clicks.TryAdd(new DesktopClick(value.Point, value.Time, IsConfiguredModifierDown())); }
                        catch (InvalidOperationException) { }
                    }
                    _leftButtonDown = false;
                    _dragged = false;
                    break;
            }
        }
        return NativeMethods.CallNextHookEx(_mouseHook, code, message, data);
    }

    private void ProcessDesktopClicks()
    {
        DesktopClick? previous = null;
        try
        {
            foreach (var click in _clicks.GetConsumingEnumerable())
            {
                if (!IsEmptyDesktopPoint(click.Point))
                {
                    previous = null;
                    continue;
                }

                if (previous is { } first && IsDoubleClick(first, click))
                {
                    previous = null;
                    _dispatcher.BeginInvoke(() => ToggleIcons(click.ModifierDown));
                }
                else
                    previous = click;
            }
        }
        catch (ObjectDisposedException) { }
    }

    private static bool IsDoubleClick(DesktopClick first, DesktopClick second)
    {
        var size = Forms.SystemInformation.DoubleClickSize;
        return unchecked(second.Time - first.Time) <= NativeMethods.GetDoubleClickTime() &&
               Math.Abs(second.Point.X - first.Point.X) <= Math.Max(1, size.Width / 2) &&
               Math.Abs(second.Point.Y - first.Point.Y) <= Math.Max(1, size.Height / 2);
    }

    private static bool IsEmptyDesktopPoint(NativeMethods.Point point)
    {
        if (!TryGetDesktopView(out var view, out var listView)) return false;
        var target = NativeMethods.WindowFromPoint(point);
        if (target == 0) return false;

        if (listView != 0 && (target == listView || NativeMethods.IsChild(listView, target)))
        {
            try
            {
                var element = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
                for (var current = element; current is not null; current = TreeWalker.RawViewWalker.GetParent(current))
                {
                    if (Equals(current.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty), ControlType.ListItem))
                        return false;
                    var handle = current.Current.NativeWindowHandle;
                    if (handle != 0 && handle == unchecked((int)listView.ToInt64())) break;
                }
                return true;
            }
            catch (ElementNotAvailableException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (COMException) { return false; }
        }

        if (target == view || NativeMethods.IsChild(view, target)) return true;
        var className = GetClassName(target);
        return className is "Progman" or "WorkerW";
    }

    private void SynchronizeThumbnailVisibility()
    {
        if (!TryGetIconsVisible(out var visible)) return;
        if (visible && _modifierThumbnailFollow)
        {
            _modifierThumbnailFollow = false;
            UpdateStateMonitoring();
        }
        SynchronizeThumbnailVisibility(visible);
    }

    private void SynchronizeThumbnailVisibility(bool iconsVisible)
    {
        var thumbnailsFollowIcons = _store.Current.HideThumbnailsWithDesktopIcons || _modifierThumbnailFollow;
        _manager.SetThumbnailsVisible(!thumbnailsFollowIcons || iconsVisible);
    }

    private void UpdateStateMonitoring()
    {
        if (_store.Current.HideThumbnailsWithDesktopIcons || _modifierThumbnailFollow)
            _stateTimer.Start();
        else
            _stateTimer.Stop();
    }

    private bool IsConfiguredModifierDown()
    {
        var virtualKey = _store.Current.DesktopIconsModifier switch
        {
            DesktopIconModifier.Shift => 0x10,
            DesktopIconModifier.Alt => 0x12,
            _ => 0x11
        };
        return (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static bool TryGetIconsVisible(out bool visible)
    {
        visible = true;
        if (!TryGetDesktopView(out _, out var listView) || listView == 0) return false;
        visible = NativeMethods.IsWindowVisible(listView);
        return true;
    }

    private static bool TryGetDesktopView(out nint view, out nint listView)
    {
        view = 0;
        listView = 0;
        var progman = NativeMethods.FindWindow("Progman", null);
        if (progman != 0) view = NativeMethods.FindWindowEx(progman, 0, "SHELLDLL_DefView", null);
        if (view == 0)
        {
            nint discoveredView = 0;
            NativeMethods.EnumWindows((window, _) =>
            {
                var candidate = NativeMethods.FindWindowEx(window, 0, "SHELLDLL_DefView", null);
                if (candidate == 0) return true;
                discoveredView = candidate;
                return false;
            }, 0);
            view = discoveredView;
        }
        if (view == 0) return false;
        listView = NativeMethods.FindWindowEx(view, 0, "SysListView32", "FolderView");
        if (listView == 0) listView = NativeMethods.FindWindowEx(view, 0, "SysListView32", null);
        return true;
    }

    private static string GetClassName(nint window)
    {
        var name = new StringBuilder(64);
        return NativeMethods.GetClassName(window, name, name.Capacity) > 0 ? name.ToString() : "";
    }

    private void ReleaseMouseHook()
    {
        if (_mouseHook == 0) return;
        NativeMethods.UnhookWindowsHookEx(_mouseHook);
        _mouseHook = 0;
        _leftButtonDown = false;
        _dragged = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stateTimer.Stop();
        ReleaseMouseHook();
        _clicks.CompleteAdding();
        if (!_hitTestThread.Join(1000)) _hitTestThread.Interrupt();
        _clicks.Dispose();
    }

    private readonly record struct DesktopClick(NativeMethods.Point Point, uint Time, bool ModifierDown);

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        internal NativeMethods.Point Point;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal nint ExtraInfo;
    }
}
