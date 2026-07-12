using System.Drawing;
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
    private Forms.ToolStripItem? _settingsMenuItem;
    private Forms.ToolStripItem? _exitMenuItem;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _store = new SettingsStore();
        _store.Load();
        _manager = new WindowManager(_store);
        _manager.Start();

        var menu = new Forms.ContextMenuStrip();
        _settingsMenuItem = menu.Items.Add("", null, (_, _) => ShowSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        _exitMenuItem = menu.Items.Add("", null, (_, _) => Shutdown());
        try
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
                _trayIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath);
        }
        catch { _trayIcon = null; }
        _tray = new Forms.NotifyIcon
        {
            Text = "Minimemizer",
            Icon = _trayIcon ?? SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => ShowSettings();
        UpdateTrayLanguage();
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
            Dispatcher.BeginInvoke(ShowSettings);
    }

    private void DisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.Invoke(() => _manager?.Relayout());

    private void ShowSettings()
    {
        if (_store is null || _manager is null) return;
        if (_settingsWindow is { IsVisible: true }) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(_store, _manager);
        _settingsWindow.LanguageChanged += (_, _) => UpdateTrayLanguage();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void UpdateTrayLanguage()
    {
        if (_store is null) return;
        if (_settingsMenuItem is not null) _settingsMenuItem.Text = Localizer.T(_store.Current.Language, "Indstillinger");
        if (_exitMenuItem is not null) _exitMenuItem.Text = Localizer.T(_store.Current.Language, "Afslut");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
        _manager?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
