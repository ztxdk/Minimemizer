using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using FontFamily = System.Windows.Media.FontFamily;
using Application = System.Windows.Application;

namespace Minimemizer;

internal sealed class TrayMenuWindow : Window
{
    private bool _isClosing;
    private ContextMenu? _rescueMenu;

    internal TrayMenuWindow(AppLanguage language, bool iconsVisible, IReadOnlyList<OffscreenWindowCandidate> rescueCandidates,
        Action toggleDesktopIcons, Action rescueAll, Action<OffscreenWindowCandidate> rescueWindow,
        Action openSettings, Action exit)
    {
        Width = 238;
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

        var stack = new StackPanel { Margin = new Thickness(4) };
        stack.Children.Add(MenuButton("▦", Localizer.T(language, iconsVisible ? "Skjul skrivebordsikoner" : "Vis skrivebordsikoner"), foreground, hover, () => RunAndClose(toggleDesktopIcons)));
        stack.Children.Add(new Border { Height = 1, Background = borderBrush, Margin = new Thickness(7, 2, 7, 2) });
        Border rescueButton = null!;
        rescueButton = MenuButton("↗", $"{Localizer.T(language, "Red vinduer")} ({rescueCandidates.Count})", foreground, hover,
            () => ShowRescueMenu(rescueButton, language, rescueCandidates, rescueAll, rescueWindow));
        stack.Children.Add(rescueButton);
        stack.Children.Add(new Border { Height = 1, Background = borderBrush, Margin = new Thickness(7, 2, 7, 2) });
        stack.Children.Add(MenuButton("⚙", Localizer.T(language, "Indstillinger"), foreground, hover, () => RunAndClose(openSettings)));
        stack.Children.Add(new Border { Height = 1, Background = borderBrush, Margin = new Thickness(7, 2, 7, 2) });
        stack.Children.Add(MenuButton("⏻", Localizer.T(language, "Afslut"), foreground, hover, () => RunAndClose(exit)));

        Content = new Border
        {
            Background = background,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = dark ? .42 : .2 },
            Child = stack
        };
    }

    private void ShowRescueMenu(Border anchor, AppLanguage language,
        IReadOnlyList<OffscreenWindowCandidate> candidates, Action rescueAll,
        Action<OffscreenWindowCandidate> rescueWindow)
    {
        if (_rescueMenu is { IsOpen: true })
        {
            _rescueMenu.IsOpen = false;
            return;
        }

        var menu = FluentMenu.Create(IsDarkMode());
        var moveAll = new MenuItem
        {
            Header = Localizer.T(language, "Flyt alle til primær skærm"),
            IsEnabled = candidates.Count > 0
        };
        moveAll.Click += (_, _) => RunAndClose(rescueAll);
        menu.Items.Add(moveAll);
        menu.Items.Add(new Separator());

        if (candidates.Count == 0)
        {
            menu.Items.Add(new MenuItem
            {
                Header = Localizer.T(language, "Ingen vinduer uden for skærmen"),
                IsEnabled = false
            });
        }
        else
        {
            foreach (var item in BuildCandidateLabels(candidates))
            {
                var label = new TextBlock
                {
                    Text = item.Label,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Width = 360,
                    ToolTip = item.Candidate.Title
                };
                var menuItem = new MenuItem { Header = label };
                menuItem.Click += (_, _) => RunAndClose(() => rescueWindow(item.Candidate));
                menu.Items.Add(menuItem);
            }
        }

        _rescueMenu = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(_rescueMenu, menu)) _rescueMenu = null; };
        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.Left;
        menu.HorizontalOffset = -4;
        menu.IsOpen = true;
    }

    private static IReadOnlyList<(OffscreenWindowCandidate Candidate, string Label)> BuildCandidateLabels(
        IReadOnlyList<OffscreenWindowCandidate> candidates)
    {
        var titleCounts = candidates
            .GroupBy(candidate => candidate.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.CurrentCultureIgnoreCase);
        var used = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        var result = new List<(OffscreenWindowCandidate, string)>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var label = candidate.Title;
            if (titleCounts[candidate.Title] > 1 && !string.IsNullOrWhiteSpace(candidate.ProcessName))
                label = $"{label} — {candidate.ProcessName}";
            used.TryGetValue(label, out var number);
            number++;
            used[label] = number;
            if (number > 1) label = $"{label} ({number})";
            result.Add((candidate, label));
        }
        return result;
    }

    private static Border MenuButton(string glyph, string text, Brush foreground, Brush hover, Action action)
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.Children.Add(new TextBlock { Text = glyph, Foreground = foreground, FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
        var label = new TextBlock { Text = text, Foreground = foreground, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1); content.Children.Add(label);
        content.Margin = new Thickness(3, 0, 7, 0);
        var button = new Border { Child = content, Height = 34, Background = Brushes.Transparent, CornerRadius = new CornerRadius(5), Cursor = System.Windows.Input.Cursors.Hand };
        var normal = Brushes.Transparent;
        button.MouseEnter += (_, _) => button.Background = hover;
        button.MouseLeave += (_, _) => button.Background = normal;
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; action(); };
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
        Deactivated += (_, _) => Dismiss();
    }

    private void RunAndClose(Action action)
    {
        Dismiss();
        Application.Current.Dispatcher.BeginInvoke(action);
    }

    private void Dismiss()
    {
        if (_isClosing) return;
        _isClosing = true;
        if (_rescueMenu is not null) _rescueMenu.IsOpen = false;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_rescueMenu is not null) _rescueMenu.IsOpen = false;
        base.OnClosed(e);
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
