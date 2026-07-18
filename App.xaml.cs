using System.Drawing;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace Minimemizer;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\Minimemizer.SingleInstance";
    private const string ExitEventName = @"Local\Minimemizer.ExitRequested";
    private Forms.NotifyIcon? _tray;
    private WindowManager? _manager;
    private SettingsStore? _store;
    private SettingsWindow? _settingsWindow;
    private RuntimeStateStore? _runtimeState;
    private UpdateService? _updates;
    private Icon? _trayIcon;
    private TrayMenuWindow? _trayMenuWindow;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _exitRequestedEvent;
    private RegisteredWaitHandle? _exitWaitRegistration;
    private bool _ownsInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (InstallationManager.TryRunMaintenance(e.Args))
        {
            Shutdown();
            return;
        }
        _store = new SettingsStore();
        _store.Load();
        _runtimeState = new RuntimeStateStore();
        _runtimeState.Load();
        if (e.Args.Contains("--request-uninstall", StringComparer.OrdinalIgnoreCase))
        {
            HandleUninstallRequest();
            return;
        }
        if (!EnsureSingleInstance())
        {
            Shutdown();
            return;
        }
        if (!e.Args.Contains("--portable", StringComparer.OrdinalIgnoreCase) && InstallationManager.ShouldOfferInstallation(_runtimeState))
        {
            var firstRun = new FirstRunWindow(_store.Current.Language);
            if (firstRun.ShowDialog() != true) { Shutdown(); return; }
            if (firstRun.Choice is { Scope: InstallationScope.Portable })
                _runtimeState.DismissPortablePrompt(InstallationManager.CurrentExecutable);
            else if (firstRun.Choice is { } choice)
            {
                if (InstallationManager.BeginInstall(choice.Scope, choice.StartMenuShortcut, choice.DesktopShortcut))
                {
                    Shutdown();
                    return;
                }
                MessageBox.Show(Localizer.T(_store.Current.Language, "Installationen kunne ikke startes."), "Minimemizer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        StartExitListener();
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
        _tray.BalloonTipClicked += (_, _) => Dispatcher.Invoke(() => ShowSettings("about"));
        _updates = new UpdateService(_store, _runtimeState);
        _updates.UpdateAvailable += UpdateAvailable;
        _tray.DoubleClick += (_, _) => ShowSettings();
        _tray.MouseUp += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Right)
                Dispatcher.Invoke(ShowTrayMenu);
        };
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
            Dispatcher.BeginInvoke(ShowSettings);
        if (e.Args.Contains("--about", StringComparer.OrdinalIgnoreCase))
            Dispatcher.BeginInvoke(() => ShowSettings("about"));
        if (e.Args.Contains("--tray-menu", StringComparer.OrdinalIgnoreCase))
            Dispatcher.BeginInvoke(ShowTrayMenu);
        WriteMaintenanceHealthMarker(e.Args);
        BeginMaintenanceCleanup(e.Args);
        _ = _updates.CheckAutomaticallyAsync();
    }

    private static void WriteMaintenanceHealthMarker(string[] args)
    {
        var index = Array.FindIndex(args, value => value.Equals("--maintenance-health", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length) return;
        try { File.WriteAllText(args[index + 1], "ok"); }
        catch { }
    }

    private static void BeginMaintenanceCleanup(string[] args)
    {
        var index = Array.FindIndex(args, value => value.Equals("--maintenance-cleanup", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 2 >= args.Length || !int.TryParse(args[index + 2], out var helperPid)) return;
        var source = args[index + 1];
        _ = Task.Run(() =>
        {
            try
            {
                using var helper = Process.GetProcessById(helperPid);
                helper.WaitForExit(15000);
            }
            catch (ArgumentException) { }
            try
            {
                if (File.Exists(source)) File.Delete(source);
                var directory = Path.GetDirectoryName(source);
                if (directory is not null && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
            catch { }
        });
    }

    private void HandleUninstallRequest()
    {
        if (_store is null) { Shutdown(); return; }
        var installation = InstallationManager.DetectCurrent();
        if (!installation.IsInstalled) { Shutdown(); return; }
        var language = _store.Current.Language;
        if (MessageBox.Show(Localizer.T(language, "Vil du afinstallere Minimemizer?"), "Minimemizer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        { Shutdown(); return; }
        var delete = MessageBox.Show(Localizer.T(language, "Vil du også slette dine personlige indstillinger?"), "Minimemizer", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        InstallationManager.BeginUninstall(installation, delete);
        Shutdown();
    }

    private void UpdateAvailable(object? sender, AvailableUpdate update) => Dispatcher.BeginInvoke(() =>
    {
        if (_tray is null || _store is null) return;
        _tray.BalloonTipTitle = "Minimemizer";
        _tray.BalloonTipText = string.Format(Localizer.T(_store.Current.Language, "Version {0} er tilgængelig."), update.Version.ToString(3));
        _tray.ShowBalloonTip(5000);
    });

    private void DisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.Invoke(() => _manager?.Relayout());

    private bool EnsureSingleInstance()
    {
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
        _ownsInstanceMutex = createdNew;
        if (createdNew && !HasOtherMinimemizerProcesses()) return true;

        var language = _store?.Current.Language ?? AppLanguage.English;
        var message = $"{Localizer.T(language, "Minimemizer kører allerede.")}\n\n{Localizer.T(language, "Vil du lukke den kørende udgave og starte denne?")}";
        if (MessageBox.Show(message, "Minimemizer", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return false;

        if (createdNew)
        {
            CloseLegacyInstances();
            if (!HasOtherMinimemizerProcesses()) return true;
            MessageBox.Show(Localizer.T(language, "Den kørende udgave kunne ikke lukkes."), "Minimemizer", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        if (TryRequestGracefulExit() && TryAcquireInstanceMutex(TimeSpan.FromSeconds(5))) return true;

        CloseLegacyInstances();
        if (TryAcquireInstanceMutex(TimeSpan.FromSeconds(5))) return true;

        MessageBox.Show(Localizer.T(language, "Den kørende udgave kunne ikke lukkes."), "Minimemizer", MessageBoxButton.OK, MessageBoxImage.Error);
        return false;
    }

    private static bool TryRequestGracefulExit()
    {
        try
        {
            using var exitEvent = EventWaitHandle.OpenExisting(ExitEventName);
            exitEvent.Set();
            return true;
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private bool TryAcquireInstanceMutex(TimeSpan timeout)
    {
        if (_instanceMutex is null) return false;
        try { _ownsInstanceMutex = _instanceMutex.WaitOne(timeout); }
        catch (AbandonedMutexException) { _ownsInstanceMutex = true; }
        return _ownsInstanceMutex;
    }

    private static void CloseLegacyInstances()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (!IsOtherMinimemizerProcess(process, current.Id)) continue;
                try
                {
                    if (process.HasExited) continue;
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                catch (NotSupportedException) { }
            }
        }
    }

    private static bool HasOtherMinimemizerProcesses()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (!IsOtherMinimemizerProcess(process, current.Id)) continue;
                try { if (!process.HasExited) return true; }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        return false;
    }

    private static bool IsOtherMinimemizerProcess(Process process, int currentProcessId)
    {
        if (process.Id == currentProcessId) return false;
        try
        {
            return string.Equals(process.ProcessName, "Minimemizer", StringComparison.OrdinalIgnoreCase) ||
                   process.ProcessName.StartsWith("Minimemizer-", StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private void StartExitListener()
    {
        _exitRequestedEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        _exitWaitRegistration = ThreadPool.RegisterWaitForSingleObject(_exitRequestedEvent, (_, timedOut) =>
        {
            if (!timedOut) Dispatcher.BeginInvoke(new Action(Shutdown));
        }, null, Timeout.Infinite, executeOnlyOnce: true);
    }

    private void ShowSettings(string initialPage = "general")
    {
        if (_store is null || _manager is null) return;
        if (_settingsWindow is { IsVisible: true }) { _settingsWindow.Activate(); return; }
        if (_updates is null) return;
        _settingsWindow = new SettingsWindow(_store, _manager, _updates, initialPage);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ShowTrayMenu()
    {
        if (_store is null) return;
        _trayMenuWindow?.Close();
        _trayMenuWindow = new TrayMenuWindow(_store.Current.Language, () => ShowSettings(), Shutdown);
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
        if (_updates is not null) _updates.UpdateAvailable -= UpdateAvailable;
        _updates?.Dispose();
        _exitWaitRegistration?.Unregister(null);
        _exitRequestedEvent?.Dispose();
        if (_ownsInstanceMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
