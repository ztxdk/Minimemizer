using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Application = System.Windows.Application;
using DrawingPoint = System.Drawing.Point;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;

namespace Minimemizer;

public sealed class WindowManager : IDisposable
{
    private readonly SettingsStore _store;
    private readonly TaskViewOwnerWindow _taskViewOwner;
    private readonly Dictionary<nint, ThumbnailWindow> _thumbnails = [];
    private readonly Dictionary<nint, WindowZoneOverride> _windowZones = [];
    private readonly List<ZoneOverlayWindow> _zoneOverlays = [];
    private readonly NativeMethods.WinEventDelegate _callback;
    private nint _minimizeHook;
    private nint _destroyHook;
    private nint _cloakHook;
    private readonly DispatcherTimer _scanTimer;
    private ThumbnailWindow? _draggedWindow;
    private bool _disposed;

    public WindowManager(SettingsStore store)
    {
        _store = store;
        _taskViewOwner = new TaskViewOwnerWindow();
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
        _cloakHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_CLOAKED, NativeMethods.EVENT_OBJECT_UNCLOAKED, 0, _callback, 0, 0, flags);
        NativeMethods.EnumWindows((hwnd, _) => { if (NativeMethods.IsIconic(hwnd)) Add(hwnd); return true; }, 0);
        _scanTimer.Start();
    }

    private void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (hwnd == 0 || _disposed) return;
        var objectEvent = eventType is NativeMethods.EVENT_OBJECT_DESTROY
            or NativeMethods.EVENT_OBJECT_CLOAKED
            or NativeMethods.EVENT_OBJECT_UNCLOAKED;
        if (objectEvent && (idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF)) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            if (eventType == NativeMethods.EVENT_SYSTEM_MINIMIZESTART)
            {
                if (NativeMethods.IsIconic(hwnd) && !NativeMethods.IsWindowCloaked(hwnd)) Add(hwnd);
            }
            else if (eventType == NativeMethods.EVENT_SYSTEM_MINIMIZEEND)
            {
                if (!NativeMethods.IsIconic(hwnd)) Remove(hwnd);
            }
            else if (eventType == NativeMethods.EVENT_OBJECT_DESTROY)
                Remove(hwnd, discardOverride: true);
            else if (eventType == NativeMethods.EVENT_OBJECT_CLOAKED)
                Remove(hwnd);
            else if (eventType == NativeMethods.EVENT_OBJECT_UNCLOAKED && NativeMethods.IsIconic(hwnd))
                Add(hwnd);
        });
    }

    private bool Add(nint hwnd, bool relayout = true)
    {
        if (_disposed || !NativeMethods.IsIconic(hwnd) || !IsEligible(hwnd)) return false;
        if (_thumbnails.TryGetValue(hwnd, out var existing))
        {
            if (existing.MatchesSource(hwnd)) return false;
            Remove(hwnd, relayout: false, discardOverride: true);
        }

        var titleBuffer = new StringBuilder(512);
        NativeMethods.GetWindowText(hwnd, titleBuffer, titleBuffer.Capacity);
        var window = new ThumbnailWindow(hwnd, titleBuffer.ToString(), NativeMethods.GetProcessPath(hwnd),
            _taskViewOwner.WindowHandle);
        window.DragStarted += OnDragStarted;
        window.DragMoved += OnDragMoved;
        window.DragCompleted += OnDragCompleted;
        window.ZoneMenuRequested += ShowZoneMenu;
        _thumbnails.Add(hwnd, window);
        window.Show();
        if (!window.HasRegisteredThumbnail || !NativeMethods.IsWindow(hwnd) || !NativeMethods.IsIconic(hwnd) || NativeMethods.IsWindowCloaked(hwnd))
        {
            Remove(hwnd, relayout: false);
            return false;
        }
        ValidateOverride(window);
        if (relayout) Relayout();
        return true;
    }

    private bool IsEligible(nint hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsWindowCloaked(hwnd)) return false;
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

    private bool Remove(nint hwnd, bool relayout = true, bool discardOverride = false)
    {
        if (discardOverride) _windowZones.Remove(hwnd);
        if (!_thumbnails.Remove(hwnd, out var window)) return false;
        if (ReferenceEquals(_draggedWindow, window)) EndDrag();
        window.Close();
        if (relayout) Relayout();
        return true;
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
        if (_disposed) return;
        foreach (var hwnd in _windowZones.Keys.Where(hwnd => !NativeMethods.IsWindow(hwnd)).ToArray())
            _windowZones.Remove(hwnd);

        var changed = false;
        foreach (var pair in _thumbnails.ToArray())
        {
            var exists = NativeMethods.IsWindow(pair.Key);
            if (!exists || !NativeMethods.IsIconic(pair.Key) || !IsEligible(pair.Key) || !pair.Value.MatchesSource(pair.Key))
                changed |= Remove(pair.Key, relayout: false, discardOverride: !exists || !pair.Value.MatchesSource(pair.Key));
        }
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.IsIconic(hwnd)) changed |= Add(hwnd, relayout: false);
            return true;
        }, 0);
        if (changed) Relayout();
    }

    public void Relayout()
    {
        if (_disposed || _draggedWindow is not null || _thumbnails.Count == 0) return;
        var screens = Forms.Screen.AllScreens;
        if (screens.Length == 0) return;
        var settings = _store.Current;

        var groups = _thumbnails.Values
            .Select(window => new
            {
                Window = window,
                Zone = ThumbnailZones.Resolve(GetRequestedZone(window), settings, screens)
            })
            .GroupBy(item => item.Zone);

        foreach (var group in groups)
            LayoutZone(group.Key, group.Select(item => item.Window).Reverse().ToArray(), settings, screens);
    }

    private static void LayoutZone(ThumbnailZone zone, IReadOnlyList<ThumbnailWindow> windows, AppSettings settings, IReadOnlyList<Forms.Screen> screens)
    {
        var area = ThumbnailZones.FindScreen(zone, screens).WorkingArea;
        var items = windows
            .Select(window => (Window: window, Size: window.GetPreferredSize(settings.ThumbnailWidth, settings.ThumbnailHeight, settings.SizeMode)))
            .ToArray();
        var vertical = settings.Flow == ThumbnailFlow.Vertical;
        var zoneLength = (vertical ? area.Height : area.Width) / 2;
        var zoneCrossLength = (vertical ? area.Width : area.Height) / 2;
        var availableLength = Math.Max(1, zoneLength - 2 * settings.EdgeMargin);
        var availableCrossLength = Math.Max(1, zoneCrossLength - 2 * settings.EdgeMargin);
        var naturalLength = items.Sum(item => vertical ? item.Size.Height : item.Size.Width) + settings.Gap * Math.Max(0, items.Length - 1);
        var naturalCrossLength = items.Length == 0 ? 0 : items.Max(item => vertical ? item.Size.Width : item.Size.Height);
        var lengthScale = naturalLength > 0 ? (double)availableLength / naturalLength : 1d;
        var crossScale = naturalCrossLength > 0 ? (double)availableCrossLength / naturalCrossLength : 1d;
        var scale = Math.Min(1d, Math.Max(0.05d, Math.Min(lengthScale, crossScale)));
        var gap = (int)Math.Round(settings.Gap * scale);
        var fromRight = zone.Corner is ScreenCorner.TopRight or ScreenCorner.BottomRight;
        var fromBottom = zone.Corner is ScreenCorner.BottomLeft or ScreenCorner.BottomRight;
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
            ApplySettings(item.Window, settings);
            item.Window.ApplyBounds(x, y, width, height);
        }
    }

    private static void ApplySettings(ThumbnailWindow window, AppSettings settings)
    {
        window.SetFrameStyle(settings.FrameStyle);
        window.SetSizingMode(settings.SizeMode, settings.UniformContent);
        window.SetIconPosition(settings.IconPosition);
        window.SetThumbnailOpacity(settings.ThumbnailOpacity);
        window.SetContextMenuEnabled(settings.EnableThumbnailContextMenu);
        window.SetTitleTooltipEnabled(settings.ShowTitleOnHover);
        window.SetIconVisibility(settings.ShowProgramIcon);
        window.SetRestoreOnSingleClick(settings.RestoreOnSingleClick);
    }

    private ThumbnailZone GetRequestedZone(ThumbnailWindow window)
    {
        if (_windowZones.TryGetValue(window.SourceHandle, out var windowOverride))
        {
            if (windowOverride.ProcessId == window.SourceProcessId &&
                string.Equals(windowOverride.ExecutablePath, window.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                return windowOverride.Zone;
            _windowZones.Remove(window.SourceHandle);
        }

        var rule = _store.Current.ProgramZoneRules.LastOrDefault(rule =>
            string.Equals(rule.ExecutablePath, window.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        return rule is null ? ThumbnailZones.Default(_store.Current) : ThumbnailZones.FromRule(rule);
    }

    private void ValidateOverride(ThumbnailWindow window) => _ = GetRequestedZone(window);

    private void SetWindowZone(ThumbnailWindow window, ThumbnailZone zone)
    {
        _windowZones[window.SourceHandle] = new WindowZoneOverride(window.SourceProcessId, window.ExecutablePath, zone);
    }

    private void OnDragStarted(ThumbnailWindow window, NativeMethods.Point cursor)
    {
        if (_disposed) return;
        _draggedWindow = window;
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var overlay = new ZoneOverlayWindow(screen, _store.Current.Language, _taskViewOwner.WindowHandle);
            _zoneOverlays.Add(overlay);
            overlay.Show();
        }
        UpdateDragTarget(cursor);
    }

    private void OnDragMoved(ThumbnailWindow window, NativeMethods.Point cursor)
    {
        if (ReferenceEquals(window, _draggedWindow)) UpdateDragTarget(cursor);
    }

    private void OnDragCompleted(ThumbnailWindow window, NativeMethods.Point cursor)
    {
        if (!ReferenceEquals(window, _draggedWindow)) return;
        var zone = ZoneFromPoint(cursor);
        SetWindowZone(window, zone);
        EndDrag();
        Relayout();
    }

    private void UpdateDragTarget(NativeMethods.Point cursor)
    {
        var target = ZoneFromPoint(cursor);
        foreach (var overlay in _zoneOverlays)
            overlay.Highlight(string.Equals(overlay.ScreenDeviceName, target.ScreenDeviceName, StringComparison.OrdinalIgnoreCase)
                ? target.Corner
                : null);
    }

    private static ThumbnailZone ZoneFromPoint(NativeMethods.Point cursor)
    {
        var screen = Forms.Screen.FromPoint(new DrawingPoint(cursor.X, cursor.Y));
        var area = screen.WorkingArea;
        var onLeft = cursor.X < area.Left + area.Width / 2;
        var onTop = cursor.Y < area.Top + area.Height / 2;
        var corner = (onTop, onLeft) switch
        {
            (true, true) => ScreenCorner.TopLeft,
            (true, false) => ScreenCorner.TopRight,
            (false, true) => ScreenCorner.BottomLeft,
            _ => ScreenCorner.BottomRight
        };
        return new ThumbnailZone(screen.DeviceName, corner);
    }

    private void EndDrag()
    {
        _draggedWindow = null;
        foreach (var overlay in _zoneOverlays) overlay.Close();
        _zoneOverlays.Clear();
    }

    private void ShowZoneMenu(ThumbnailWindow window, Button anchor)
    {
        if (_disposed || !_thumbnails.ContainsKey(window.SourceHandle)) return;
        var language = _store.Current.Language;
        var currentZone = ThumbnailZones.Resolve(GetRequestedZone(window), _store.Current, Forms.Screen.AllScreens);
        var menu = FluentMenu.Create(AppTheme.IsDarkModeEnabled());
        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.Bottom;
        menu.Items.Add(new MenuItem { Header = Localizer.T(language, "Flyt til zone"), IsEnabled = false });
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var display = ThumbnailZones.DisplayLabel(language, screen.DeviceName, Forms.Screen.AllScreens);
            foreach (var corner in Enum.GetValues<ScreenCorner>())
            {
                var zone = new ThumbnailZone(screen.DeviceName, corner);
                var cornerItem = new MenuItem
                {
                    Header = $"{display} · {ThumbnailZones.CornerLabel(language, corner)}",
                    IsChecked = zone == currentZone
                };
                cornerItem.Click += (_, _) => { SetWindowZone(window, zone); Relayout(); };
                menu.Items.Add(cornerItem);
            }
        }
        menu.Items.Add(new Separator());

        var moveAll = new MenuItem
        {
            Header = string.Format(Localizer.T(language, "Flyt alle åbne fra {0} hertil"), window.ProgramName)
        };
        moveAll.Click += (_, _) =>
        {
            foreach (var candidate in _thumbnails.Values.Where(candidate =>
                         string.Equals(candidate.ExecutablePath, window.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
                SetWindowZone(candidate, currentZone);
            Relayout();
        };
        menu.Items.Add(moveAll);

        var makeDefault = new MenuItem
        {
            Header = string.Format(Localizer.T(language, "Brug denne zone som standard for {0}"), window.ProgramName)
        };
        makeDefault.Click += (_, _) => SaveProgramRule(window, currentZone);
        menu.Items.Add(makeDefault);
        menu.IsOpen = true;
    }

    private void SaveProgramRule(ThumbnailWindow window, ThumbnailZone zone)
    {
        var previous = _store.Current.ProgramZoneRules
            .Select(rule => new ProgramZoneRule
            {
                ExecutablePath = rule.ExecutablePath,
                ScreenDeviceName = rule.ScreenDeviceName,
                Corner = rule.Corner
            })
            .ToList();
        _store.Current.ProgramZoneRules.RemoveAll(rule =>
            string.Equals(rule.ExecutablePath, window.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        _store.Current.ProgramZoneRules.Add(new ProgramZoneRule
        {
            ExecutablePath = window.ExecutablePath,
            ScreenDeviceName = zone.ScreenDeviceName,
            Corner = zone.Corner
        });
        try
        {
            _store.Save();
            Relayout();
        }
        catch (Exception ex)
        {
            _store.Current.ProgramZoneRules = previous;
            var message = Localizer.T(_store.Current.Language, "Indstillingerne kunne ikke gemmes:");
            MessageBox.Show($"{message}\n{ex.Message}", "Minimemizer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scanTimer.Stop();
        if (_minimizeHook != 0) NativeMethods.UnhookWinEvent(_minimizeHook);
        if (_destroyHook != 0) NativeMethods.UnhookWinEvent(_destroyHook);
        if (_cloakHook != 0) NativeMethods.UnhookWinEvent(_cloakHook);
        EndDrag();
        foreach (var window in _thumbnails.Values.ToArray()) window.Close();
        _thumbnails.Clear();
        _windowZones.Clear();
        _taskViewOwner.Dispose();
    }

    private readonly record struct WindowZoneOverride(uint ProcessId, string ExecutablePath, ThumbnailZone Zone);
}
