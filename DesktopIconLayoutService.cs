using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.IO;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Minimemizer;

internal sealed class DesktopIconLayoutService : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly DesktopIconProfileStore _profiles = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _monitorTask;
    private volatile bool _enabled;
    private volatile bool _displayChangePending;
    private int _displayChangeGeneration;
    private bool _disposed;
    private string? _activeProfileKey;
    private List<DesktopIconPosition>? _lastLayout;
    private nint _lastListView;

    internal DesktopIconLayoutService(SettingsStore settings)
    {
        _settings = settings;
        SystemEvents.DisplaySettingsChanging += DisplaySettingsChanging;
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        _monitorTask = Task.Run(MonitorAsync);
    }

    internal void ApplySettings()
    {
        if (_disposed) return;
        var enabled = _settings.Current.RememberDesktopIconPositions;
        if (enabled == _enabled) return;
        _enabled = enabled;
        if (enabled)
        {
            _displayChangePending = false;
            QueueSynchronize();
        }
        else
            QueueOperation(SaveCurrentLayout);
    }

    private async Task MonitorAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(_cancellation.Token))
            {
                if (_enabled && !_displayChangePending) await RunSerializedAsync(Synchronize);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void DisplaySettingsChanging(object? sender, EventArgs e)
    {
        if (!_enabled) return;
        Interlocked.Increment(ref _displayChangeGeneration);
        _displayChangePending = true;
    }

    private void DisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (!_enabled)
        {
            _displayChangePending = false;
            return;
        }
        var generation = Volatile.Read(ref _displayChangeGeneration);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _cancellation.Token);
                if (generation != Volatile.Read(ref _displayChangeGeneration)) return;
                _displayChangePending = false;
                if (_enabled) await RunSerializedAsync(Synchronize);
            }
            catch (OperationCanceledException) { }
        });
    }

    private void QueueSynchronize() => QueueOperation(Synchronize);

    private void QueueOperation(Action operation)
    {
        _ = Task.Run(async () =>
        {
            try { await RunSerializedAsync(operation); }
            catch (OperationCanceledException) { }
        });
    }

    private async Task RunSerializedAsync(Action operation)
    {
        await _operationGate.WaitAsync(_cancellation.Token);
        try
        {
            if (!_disposed) operation();
        }
        catch
        {
            // Explorer can restart or replace its desktop view at any time.
            // The next monitoring pass retries without interrupting the app.
        }
        finally { _operationGate.Release(); }
    }

    private void Synchronize()
    {
        if (!_enabled || _displayChangePending || !DesktopListView.TryOpen(out var desktop)) return;
        using (desktop)
        {
            if (desktop.AutoArrangeEnabled) return;
            var profileKey = DisplayProfileKey.Create();
            var viewChanged = desktop.ListView != _lastListView;
            if (!string.Equals(profileKey, _activeProfileKey, StringComparison.Ordinal) || viewChanged)
            {
                ActivateProfile(desktop, profileKey);
                return;
            }

            var current = desktop.ReadPositions();
            if (current.Count == 0 || LayoutsEqual(current, _lastLayout)) return;
            SaveProfile(profileKey, current);
            _lastLayout = current;
        }
    }

    private void ActivateProfile(DesktopListView desktop, string profileKey)
    {
        var current = desktop.ReadPositions();
        if (current.Count == 0) return;

        List<DesktopIconPosition>? desired = null;
        if (_profiles.TryGet(profileKey, out var saved))
            desired = saved;
        else if (_lastLayout is { Count: > 0 })
            desired = _lastLayout;

        if (desired is { Count: > 0 })
        {
            desktop.ApplyPositions(desired);
            Thread.Sleep(150);
            var restored = desktop.ReadPositions();
            if (restored.Count > 0) current = restored;
        }

        _activeProfileKey = profileKey;
        _lastListView = desktop.ListView;
        _lastLayout = current;
        SaveProfile(profileKey, current);
    }

    private void SaveCurrentLayout()
    {
        if (_displayChangePending || _activeProfileKey is null || !DesktopListView.TryOpen(out var desktop)) return;
        using (desktop)
        {
            var current = desktop.ReadPositions();
            if (current.Count == 0) return;
            SaveProfile(_activeProfileKey, current);
            _lastLayout = current;
        }
    }

    private void SaveProfile(string key, List<DesktopIconPosition> positions)
    {
        _profiles.Set(key, positions);
        _profiles.Save();
    }

    private static bool LayoutsEqual(IReadOnlyList<DesktopIconPosition> left, IReadOnlyList<DesktopIconPosition>? right)
    {
        if (right is null || left.Count != right.Count) return false;
        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].Name, right[index].Name, StringComparison.OrdinalIgnoreCase) ||
                left[index].X != right[index].X || left[index].Y != right[index].Y)
                return false;
        }
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _enabled = false;
        SystemEvents.DisplaySettingsChanging -= DisplaySettingsChanging;
        SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
        _cancellation.Cancel();
        try { _monitorTask.Wait(TimeSpan.FromMilliseconds(500)); }
        catch (AggregateException) { }
        _cancellation.Dispose();
    }
}

internal static class DisplayProfileKey
{
    internal static string Create()
    {
        var screens = Forms.Screen.AllScreens
            .OrderBy(screen => screen.Bounds.X)
            .ThenBy(screen => screen.Bounds.Y)
            .ThenBy(screen => screen.Bounds.Width)
            .ThenBy(screen => screen.Bounds.Height)
            .ToArray();
        var virtualScreen = Forms.SystemInformation.VirtualScreen;
        var layout = string.Join(';', screens.Select(screen =>
            $"{screen.Bounds.X},{screen.Bounds.Y},{screen.Bounds.Width},{screen.Bounds.Height},{(screen.Primary ? 1 : 0)}"));
        return $"{screens.Length}|{virtualScreen.X},{virtualScreen.Y},{virtualScreen.Width},{virtualScreen.Height}|{layout}";
    }
}

internal sealed class DesktopIconProfileStore
{
    private const int MaximumProfiles = 24;
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Minimemizer", "desktop-icon-layouts.json");
    private DesktopIconProfileDocument _document = new();

    internal DesktopIconProfileStore()
    {
        try
        {
            if (File.Exists(_path))
                _document = JsonSerializer.Deserialize<DesktopIconProfileDocument>(File.ReadAllText(_path)) ?? new();
        }
        catch { _document = new(); }
        _document.Profiles ??= new Dictionary<string, DesktopIconProfile>();
    }

    internal bool TryGet(string key, out List<DesktopIconPosition> positions)
    {
        if (_document.Profiles.TryGetValue(key, out var profile) && profile.Icons is { Count: > 0 })
        {
            positions = Normalize(profile.Icons);
            return positions.Count > 0;
        }
        positions = [];
        return false;
    }

    internal void Set(string key, List<DesktopIconPosition> positions)
    {
        _document.Profiles[key] = new DesktopIconProfile
        {
            UpdatedUtc = DateTimeOffset.UtcNow,
            Icons = Normalize(positions)
        };
        foreach (var obsolete in _document.Profiles
                     .OrderByDescending(pair => pair.Value.UpdatedUtc)
                     .Skip(MaximumProfiles)
                     .Select(pair => pair.Key)
                     .ToArray())
            _document.Profiles.Remove(obsolete);
    }

    internal void Save()
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_document, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch { }
        }
    }

    private static List<DesktopIconPosition> Normalize(IEnumerable<DesktopIconPosition> positions) => positions
        .Where(position => !string.IsNullOrWhiteSpace(position.Name))
        .GroupBy(position => position.Name, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.Last())
        .OrderBy(position => position.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

internal sealed class DesktopIconProfileDocument
{
    public int Version { get; set; } = 1;
    public Dictionary<string, DesktopIconProfile> Profiles { get; set; } = new();
}

internal sealed class DesktopIconProfile
{
    public DateTimeOffset UpdatedUtc { get; set; }
    public List<DesktopIconPosition> Icons { get; set; } = [];
}

internal sealed class DesktopIconPosition
{
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
}

internal sealed class DesktopListView : IDisposable
{
    private const uint LvmFirst = 0x1000;
    private const uint LvmGetItemCount = LvmFirst + 4;
    private const uint LvmGetItemPosition = LvmFirst + 16;
    private const uint LvmSetItemPosition32 = LvmFirst + 49;
    private const uint LvmGetItemTextW = LvmFirst + 115;
    private const int MaximumTextLength = 1024;
    private const int RemotePadding = 16;
    private const uint MessageTimeoutMilliseconds = 1000;

    private readonly nint _process;
    private readonly nint _remoteMemory;
    private readonly nint _pointAddress;
    private readonly nint _itemAddress;
    private readonly nint _textAddress;
    private readonly int _itemSize = Marshal.SizeOf<ListViewItem>();
    private readonly int _allocationSize;

    internal nint ListView { get; }
    internal bool AutoArrangeEnabled =>
        (NativeMethods.GetWindowLongPtr(ListView, NativeMethods.GWL_STYLE).ToInt64() & 0x0100) != 0;

    private DesktopListView(nint listView, nint process)
    {
        ListView = listView;
        _process = process;
        _allocationSize = RemotePadding + _itemSize + RemotePadding + MaximumTextLength * sizeof(char);
        _remoteMemory = NativeMethods.VirtualAllocEx(process, 0, (nuint)_allocationSize,
            NativeMethods.MEM_COMMIT | NativeMethods.MEM_RESERVE, NativeMethods.PAGE_READWRITE);
        if (_remoteMemory == 0) throw new InvalidOperationException("Explorer memory could not be allocated.");
        _pointAddress = _remoteMemory;
        _itemAddress = _remoteMemory + RemotePadding;
        _textAddress = _itemAddress + _itemSize + RemotePadding;
    }

    internal static bool TryOpen(out DesktopListView desktop)
    {
        desktop = null!;
        if (!DesktopIconService.TryGetDesktopView(out _, out var listView) || listView == 0) return false;
        NativeMethods.GetWindowThreadProcessId(listView, out var processId);
        if (processId == 0) return false;
        var process = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION | NativeMethods.PROCESS_VM_OPERATION |
            NativeMethods.PROCESS_VM_READ | NativeMethods.PROCESS_VM_WRITE, false, processId);
        if (process == 0) return false;
        try
        {
            desktop = new DesktopListView(listView, process);
            return true;
        }
        catch
        {
            NativeMethods.CloseHandle(process);
            return false;
        }
    }

    internal List<DesktopIconPosition> ReadPositions()
    {
        var result = new List<DesktopIconPosition>();
        if (!TrySend(LvmGetItemCount, 0, 0, out var countResult)) return result;
        var count = Math.Clamp(countResult.ToInt32(), 0, 10000);
        for (var index = 0; index < count; index++)
        {
            var name = ReadItemText(index);
            if (string.IsNullOrWhiteSpace(name) || !TryReadPosition(index, out var point)) continue;
            result.Add(new DesktopIconPosition { Name = name, X = point.X, Y = point.Y });
        }
        return result
            .GroupBy(position => position.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(position => position.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal void ApplyPositions(IReadOnlyCollection<DesktopIconPosition> positions)
    {
        var indexes = ReadItemIndexes();
        if (!NativeMethods.GetClientRect(ListView, out var bounds)) return;
        var maximumX = Math.Max(0, bounds.Right - 64);
        var maximumY = Math.Max(0, bounds.Bottom - 64);
        foreach (var position in positions)
        {
            if (!indexes.TryGetValue(position.Name, out var index)) continue;
            var point = new NativeMethods.Point
            {
                X = Math.Clamp(position.X, 0, maximumX),
                Y = Math.Clamp(position.Y, 0, maximumY)
            };
            if (!WriteStructure(_pointAddress, point)) continue;
            TrySend(LvmSetItemPosition32, (nint)index, _pointAddress, out _);
        }
    }

    private Dictionary<string, int> ReadItemIndexes()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!TrySend(LvmGetItemCount, 0, 0, out var countResult)) return result;
        var count = Math.Clamp(countResult.ToInt32(), 0, 10000);
        for (var index = 0; index < count; index++)
        {
            var name = ReadItemText(index);
            if (!string.IsNullOrWhiteSpace(name)) result.TryAdd(name, index);
        }
        return result;
    }

    private string ReadItemText(int index)
    {
        var item = new ListViewItem
        {
            ItemIndex = index,
            SubItemIndex = 0,
            Text = _textAddress,
            TextMax = MaximumTextLength
        };
        if (!WriteStructure(_itemAddress, item) || !TrySend(LvmGetItemTextW, (nint)index, _itemAddress, out var lengthResult))
            return "";
        var length = Math.Clamp(lengthResult.ToInt32(), 0, MaximumTextLength - 1);
        if (length == 0) return "";
        var bytes = new byte[length * sizeof(char)];
        return NativeMethods.ReadProcessMemory(_process, _textAddress, bytes, (nuint)bytes.Length, out var read) && read == (nuint)bytes.Length
            ? Encoding.Unicode.GetString(bytes)
            : "";
    }

    private bool TryReadPosition(int index, out NativeMethods.Point point)
    {
        point = default;
        if (!TrySend(LvmGetItemPosition, (nint)index, _pointAddress, out var succeeded) || succeeded == 0) return false;
        return ReadStructure(_pointAddress, out point);
    }

    private bool TrySend(uint message, nint wParam, nint lParam, out nint result) =>
        NativeMethods.SendMessageTimeout(ListView, message, wParam, lParam,
            NativeMethods.SMTO_ABORTIFHUNG, MessageTimeoutMilliseconds, out result) != 0;

    private bool WriteStructure<T>(nint address, T value) where T : unmanaged
    {
        var bytes = new byte[Marshal.SizeOf<T>()];
        MemoryMarshal.Write(bytes, in value);
        return NativeMethods.WriteProcessMemory(_process, address, bytes, (nuint)bytes.Length, out var written) &&
               written == (nuint)bytes.Length;
    }

    private bool ReadStructure<T>(nint address, out T value) where T : unmanaged
    {
        var bytes = new byte[Marshal.SizeOf<T>()];
        if (!NativeMethods.ReadProcessMemory(_process, address, bytes, (nuint)bytes.Length, out var read) || read != (nuint)bytes.Length)
        {
            value = default;
            return false;
        }
        value = MemoryMarshal.Read<T>(bytes);
        return true;
    }

    public void Dispose()
    {
        if (_remoteMemory != 0) NativeMethods.VirtualFreeEx(_process, _remoteMemory, 0, NativeMethods.MEM_RELEASE);
        if (_process != 0) NativeMethods.CloseHandle(_process);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ListViewItem
    {
        internal uint Mask;
        internal int ItemIndex;
        internal int SubItemIndex;
        internal uint State;
        internal uint StateMask;
        internal nint Text;
        internal int TextMax;
        internal int Image;
        internal nint Parameter;
        internal int Indent;
        internal int GroupId;
        internal uint ColumnCount;
        internal nint Columns;
        internal nint ColumnFormats;
        internal int Group;
    }
}
