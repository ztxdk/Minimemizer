using System.Drawing;
using System.Reflection;
using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Application = System.Windows.Application;

namespace Minimemizer;

public partial class App : Application
{
    private Forms.NotifyIcon? _tray;
    private WindowManager? _manager;
    private SettingsStore? _store;
    private SettingsWindow? _settingsWindow;
    private Icon? _trayIcon;
    private TrayMenuWindow? _trayMenuWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _store = new SettingsStore();
        _store.Load();
        _manager = new WindowManager(_store);
        _manager.Start();

        try
        {
            using var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Minimemizer.AppIcon.ico");
            if (iconStream is not null)
            {
                using var embeddedIcon = new Icon(iconStream);
                _trayIcon = new Icon(embeddedIcon, Forms.SystemInformation.SmallIconSize);
            }
            else if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
                _trayIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath);
        }
        catch { _trayIcon = null; }
        _tray = new Forms.NotifyIcon
        {
            Text = "Minimemizer",
            Icon = _trayIcon ?? SystemIcons.Application,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ShowSettings();
        _tray.MouseUp += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Right)
                Dispatcher.Invoke(ShowTrayMenu);
        };
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
            Dispatcher.BeginInvoke(ShowSettings);
        if (e.Args.Contains("--tray-menu", StringComparer.OrdinalIgnoreCase))
            Dispatcher.BeginInvoke(ShowTrayMenu);
    }

    private void DisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.Invoke(() => _manager?.Relayout());

    private void ShowSettings()
    {
        if (_store is null || _manager is null) return;
        if (_settingsWindow is { IsVisible: true }) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(_store, _manager);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ShowTrayMenu()
    {
        if (_store is null) return;
        _trayMenuWindow?.Close();
        _trayMenuWindow = new TrayMenuWindow(_store.Current.Language, ShowSettings, Shutdown);
        _trayMenuWindow.Closed += (_, _) => _trayMenuWindow = null;
        _trayMenuWindow.ShowNearCursor();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
        _manager?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _trayMenuWindow?.Close();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
