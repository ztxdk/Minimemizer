using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Forms = System.Windows.Forms;

namespace Minimemizer;

internal enum OffscreenWindowState
{
    Normal,
    Minimized,
    Maximized
}

internal sealed record OffscreenWindowCandidate(
    nint Handle,
    uint ProcessId,
    string Title,
    string ProcessName,
    NativeMethods.Rect Bounds,
    NativeMethods.Rect NormalBounds,
    OffscreenWindowState State);

internal readonly record struct WindowRescueResult(int Moved, int Failed);

internal sealed class OffscreenWindowService
{
    private readonly uint _currentProcessId = (uint)Environment.ProcessId;

    internal IReadOnlyList<OffscreenWindowCandidate> FindCandidates()
    {
        var screens = Forms.Screen.AllScreens;
        if (screens.Length == 0) return [];

        var visibleAreas = screens.Select(screen => ToNativeRect(screen.Bounds)).ToArray();
        var candidates = new List<OffscreenWindowCandidate>();
        NativeMethods.EnumWindows((handle, _) =>
        {
            if (TryGetCandidate(handle, visibleAreas, expectedProcessId: null, out var candidate))
                candidates.Add(candidate);
            return true;
        }, 0);

        return candidates
            .OrderBy(candidate => CenterY(candidate.Bounds))
            .ThenBy(candidate => CenterX(candidate.Bounds))
            .ThenBy(candidate => candidate.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    internal WindowRescueResult Rescue(OffscreenWindowCandidate selected)
    {
        var screens = Forms.Screen.AllScreens;
        var primary = screens.FirstOrDefault(screen => screen.Primary);
        if (primary is null) return new WindowRescueResult(0, 1);

        var visibleAreas = screens.Select(screen => ToNativeRect(screen.Bounds)).ToArray();
        if (!TryGetCandidate(selected.Handle, visibleAreas, selected.ProcessId, out var current))
            return new WindowRescueResult(0, 0);

        var targetArea = ToNativeRect(primary.WorkingArea);
        var target = CenterInArea(current.NormalBounds, targetArea);
        return Move(current, target, primary) ? new WindowRescueResult(1, 0) : new WindowRescueResult(0, 1);
    }

    internal WindowRescueResult RescueAll()
    {
        var candidates = FindCandidates();
        var primary = Forms.Screen.AllScreens.FirstOrDefault(screen => screen.Primary);
        if (primary is null) return new WindowRescueResult(0, candidates.Count);
        if (candidates.Count == 0) return new WindowRescueResult(0, 0);

        var targets = Arrange(candidates, ToNativeRect(primary.WorkingArea));
        var moved = 0;
        var failed = 0;
        foreach (var pair in targets)
        {
            if (Move(pair.Candidate, pair.Target, primary)) moved++;
            else failed++;
        }
        return new WindowRescueResult(moved, failed);
    }

    private bool TryGetCandidate(nint handle, IReadOnlyList<NativeMethods.Rect> visibleAreas, uint? expectedProcessId,
        out OffscreenWindowCandidate candidate)
    {
        candidate = null!;
        if (!NativeMethods.IsWindow(handle) || !NativeMethods.IsWindowVisible(handle) || NativeMethods.IsWindowCloaked(handle))
            return false;

        var extendedStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        if ((extendedStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return false;

        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0 || processId == _currentProcessId || expectedProcessId is not null && processId != expectedProcessId)
            return false;

        var titleBuffer = new StringBuilder(1024);
        if (NativeMethods.GetWindowText(handle, titleBuffer, titleBuffer.Capacity) == 0) return false;
        var title = titleBuffer.ToString().Trim();
        if (title.Length == 0) return false;

        var placement = CreatePlacement();
        if (!NativeMethods.GetWindowPlacement(handle, ref placement) || !IsValid(placement.NormalPosition)) return false;

        NativeMethods.Rect bounds;
        if (NativeMethods.IsIconic(handle))
            bounds = placement.NormalPosition;
        else if (NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                     out bounds, Marshal.SizeOf<NativeMethods.Rect>()) != 0 || !IsValid(bounds))
        {
            if (!NativeMethods.GetWindowRect(handle, out bounds) || !IsValid(bounds)) return false;
        }

        if (IsFullyCovered(bounds, visibleAreas)) return false;

        var state = NativeMethods.IsIconic(handle)
            ? OffscreenWindowState.Minimized
            : placement.ShowCommand == 3 ? OffscreenWindowState.Maximized : OffscreenWindowState.Normal;
        candidate = new OffscreenWindowCandidate(handle, processId, title, GetProcessName(processId), bounds,
            placement.NormalPosition, state);
        return true;
    }

    private static bool Move(OffscreenWindowCandidate candidate, NativeMethods.Rect target, Forms.Screen targetScreen)
    {
        if (!NativeMethods.IsWindow(candidate.Handle)) return false;
        NativeMethods.GetWindowThreadProcessId(candidate.Handle, out var processId);
        if (processId != candidate.ProcessId) return false;

        var placement = CreatePlacement();
        if (!NativeMethods.GetWindowPlacement(candidate.Handle, ref placement)) return false;
        var placementTarget = ToPlacementCoordinates(target, targetScreen);
        placement.NormalPosition = placementTarget;
        if (candidate.State == OffscreenWindowState.Maximized)
            placement.MaxPosition = new NativeMethods.Point { X = placementTarget.Left, Y = placementTarget.Top };
        return NativeMethods.SetWindowPlacement(candidate.Handle, ref placement);
    }

    private static IReadOnlyList<(OffscreenWindowCandidate Candidate, NativeMethods.Rect Target)> Arrange(
        IReadOnlyList<OffscreenWindowCandidate> candidates, NativeMethods.Rect area)
    {
        var widths = candidates.Select(candidate => Math.Min(Width(candidate.NormalBounds), Width(area))).ToArray();
        var heights = candidates.Select(candidate => Math.Min(Height(candidate.NormalBounds), Height(area))).ToArray();
        var sourceCentersX = candidates.Select(candidate => CenterX(candidate.Bounds)).ToArray();
        var sourceCentersY = candidates.Select(candidate => CenterY(candidate.Bounds)).ToArray();
        var minSourceX = sourceCentersX.Min();
        var maxSourceX = sourceCentersX.Max();
        var minSourceY = sourceCentersY.Min();
        var maxSourceY = sourceCentersY.Max();
        var maxHalfWidth = widths.Max() / 2d;
        var maxHalfHeight = heights.Max() / 2d;
        var targetMinX = area.Left + maxHalfWidth;
        var targetMaxX = area.Right - maxHalfWidth;
        var targetMinY = area.Top + maxHalfHeight;
        var targetMaxY = area.Bottom - maxHalfHeight;

        var result = new List<(OffscreenWindowCandidate, NativeMethods.Rect)>(candidates.Count);
        for (var index = 0; index < candidates.Count; index++)
        {
            var centerX = Map(sourceCentersX[index], minSourceX, maxSourceX, targetMinX, targetMaxX);
            var centerY = Map(sourceCentersY[index], minSourceY, maxSourceY, targetMinY, targetMaxY);
            var left = (int)Math.Round(centerX - widths[index] / 2d);
            var top = (int)Math.Round(centerY - heights[index] / 2d);
            left = Math.Clamp(left, area.Left, area.Right - widths[index]);
            top = Math.Clamp(top, area.Top, area.Bottom - heights[index]);
            result.Add((candidates[index], new NativeMethods.Rect
            {
                Left = left,
                Top = top,
                Right = left + widths[index],
                Bottom = top + heights[index]
            }));
        }
        return result;
    }

    private static NativeMethods.Rect CenterInArea(NativeMethods.Rect source, NativeMethods.Rect area)
    {
        var width = Math.Min(Width(source), Width(area));
        var height = Math.Min(Height(source), Height(area));
        var left = area.Left + (Width(area) - width) / 2;
        var top = area.Top + (Height(area) - height) / 2;
        return new NativeMethods.Rect { Left = left, Top = top, Right = left + width, Bottom = top + height };
    }

    private static bool IsFullyCovered(NativeMethods.Rect bounds, IReadOnlyList<NativeMethods.Rect> visibleAreas)
    {
        var uncovered = new List<NativeMethods.Rect> { bounds };
        foreach (var area in visibleAreas)
        {
            var next = new List<NativeMethods.Rect>();
            foreach (var rect in uncovered) Subtract(rect, area, next);
            uncovered = next;
            if (uncovered.Count == 0) return true;
        }
        return false;
    }

    private static void Subtract(NativeMethods.Rect source, NativeMethods.Rect cover, ICollection<NativeMethods.Rect> output)
    {
        var left = Math.Max(source.Left, cover.Left);
        var top = Math.Max(source.Top, cover.Top);
        var right = Math.Min(source.Right, cover.Right);
        var bottom = Math.Min(source.Bottom, cover.Bottom);
        if (left >= right || top >= bottom)
        {
            output.Add(source);
            return;
        }

        AddIfValid(output, source.Left, source.Top, source.Right, top);
        AddIfValid(output, source.Left, bottom, source.Right, source.Bottom);
        AddIfValid(output, source.Left, top, left, bottom);
        AddIfValid(output, right, top, source.Right, bottom);
    }

    private static void AddIfValid(ICollection<NativeMethods.Rect> output, int left, int top, int right, int bottom)
    {
        if (left < right && top < bottom)
            output.Add(new NativeMethods.Rect { Left = left, Top = top, Right = right, Bottom = bottom });
    }

    private static NativeMethods.WindowPlacement CreatePlacement() => new()
    {
        Length = (uint)Marshal.SizeOf<NativeMethods.WindowPlacement>()
    };

    private static NativeMethods.Rect ToNativeRect(System.Drawing.Rectangle rectangle) => new()
    {
        Left = rectangle.Left,
        Top = rectangle.Top,
        Right = rectangle.Right,
        Bottom = rectangle.Bottom
    };

    private static NativeMethods.Rect ToPlacementCoordinates(NativeMethods.Rect screenRect, Forms.Screen screen)
    {
        // WINDOWPLACEMENT uses workspace coordinates for regular top-level windows.
        var offsetX = screen.WorkingArea.Left - screen.Bounds.Left;
        var offsetY = screen.WorkingArea.Top - screen.Bounds.Top;
        return new NativeMethods.Rect
        {
            Left = screenRect.Left - offsetX,
            Top = screenRect.Top - offsetY,
            Right = screenRect.Right - offsetX,
            Bottom = screenRect.Bottom - offsetY
        };
    }

    private static bool IsValid(NativeMethods.Rect rect) => rect.Right > rect.Left && rect.Bottom > rect.Top;
    private static int Width(NativeMethods.Rect rect) => rect.Right - rect.Left;
    private static int Height(NativeMethods.Rect rect) => rect.Bottom - rect.Top;
    private static double CenterX(NativeMethods.Rect rect) => rect.Left + Width(rect) / 2d;
    private static double CenterY(NativeMethods.Rect rect) => rect.Top + Height(rect) / 2d;

    private static double Map(double value, double sourceMin, double sourceMax, double targetMin, double targetMax)
    {
        if (targetMax <= targetMin) return (targetMin + targetMax) / 2d;
        if (Math.Abs(sourceMax - sourceMin) < .5) return (targetMin + targetMax) / 2d;
        return targetMin + (value - sourceMin) / (sourceMax - sourceMin) * (targetMax - targetMin);
    }

    private static string GetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException) { return ""; }
        catch (InvalidOperationException) { return ""; }
        catch (System.ComponentModel.Win32Exception) { return ""; }
    }
}
