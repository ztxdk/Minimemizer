using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Minimemizer;

internal enum InstallationScope { Portable, CurrentUser, AllUsers }

internal sealed record InstallationInfo(InstallationScope Scope, string ExecutablePath)
{
    internal bool IsInstalled => Scope != InstallationScope.Portable;
}

internal static class InstallationManager
{
    private const string ProductKey = @"Software\Minimemizer";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Minimemizer";

    internal static string CurrentExecutable => Path.GetFullPath(Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unavailable."));

    internal static string GetInstallPath(InstallationScope scope) => scope switch
    {
        InstallationScope.CurrentUser => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Minimemizer", "Minimemizer.exe"),
        InstallationScope.AllUsers => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Minimemizer", "Minimemizer.exe"),
        _ => CurrentExecutable
    };

    internal static InstallationInfo DetectCurrent()
    {
        var current = CurrentExecutable;
        foreach (var scope in new[] { InstallationScope.CurrentUser, InstallationScope.AllUsers })
        {
            try
            {
                using var root = OpenRoot(scope, writable: false);
                using var key = root.OpenSubKey(ProductKey);
                if (key?.GetValue("InstallPath") is string path &&
                    string.Equals(Path.GetFullPath(path), current, StringComparison.OrdinalIgnoreCase))
                    return new InstallationInfo(scope, current);
            }
            catch { }
        }
        return new InstallationInfo(InstallationScope.Portable, current);
    }

    internal static bool ShouldOfferInstallation(RuntimeStateStore state)
    {
        if (DetectCurrent().IsInstalled || Debugger.IsAttached) return false;
        return !state.IsPortablePromptDismissed(CurrentExecutable);
    }

    internal static bool BeginInstall(InstallationScope scope, bool startMenuShortcut, bool desktopShortcut)
    {
        if (scope == InstallationScope.Portable) return false;
        var target = GetInstallPath(scope);
        var source = StageMaintenanceExecutable(CurrentExecutable, scope);
        return StartMaintenance(source, "--maintenance-install", target, scope, startMenuShortcut, desktopShortcut);
    }

    internal static bool BeginUpdate(string stagedExecutable, InstallationInfo installation)
    {
        var helper = StageMaintenanceExecutable(stagedExecutable, installation.Scope);
        var started = StartMaintenance(helper, "--maintenance-update", installation.ExecutablePath, installation.Scope, false, false);
        if (started)
        {
            try { File.Delete(stagedExecutable); }
            catch { }
        }
        return started;
    }

    internal static bool BeginUninstall(InstallationInfo installation, bool deleteSettings)
    {
        if (!installation.IsInstalled) return false;
        var started = StartMaintenance(CurrentExecutable, "--maintenance-uninstall", installation.ExecutablePath, installation.Scope, false, false);
        if (started && deleteSettings) DeleteCurrentUserData();
        return started;
    }

    private static bool StartMaintenance(string executable, string command, string target, InstallationScope scope, bool option1, bool option2)
    {
        try
        {
            var info = new ProcessStartInfo(executable) { UseShellExecute = true };
            info.ArgumentList.Add(command);
            info.ArgumentList.Add(target);
            info.ArgumentList.Add(scope.ToString());
            info.ArgumentList.Add(Environment.ProcessId.ToString());
            info.ArgumentList.Add(option1.ToString());
            info.ArgumentList.Add(option2.ToString());
            if (scope == InstallationScope.AllUsers) info.Verb = "runas";
            Process.Start(info);
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static string StageMaintenanceExecutable(string source, InstallationScope scope)
    {
        var root = scope == InstallationScope.AllUsers
            ? Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments)
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(root, "Minimemizer", "Staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var helper = Path.Combine(directory, $"mm-maintenance-{Guid.NewGuid():N}.exe");
        File.Copy(source, helper, true);
        return helper;
    }

    internal static bool TryRunMaintenance(string[] args)
    {
        if (args.Length < 6 || !args[0].StartsWith("--maintenance-", StringComparison.OrdinalIgnoreCase)) return false;
        var command = args[0];
        var target = Path.GetFullPath(args[1]);
        if (!Enum.TryParse<InstallationScope>(args[2], true, out var scope) ||
            !int.TryParse(args[3], out var parentPid) ||
            !bool.TryParse(args[4], out var option1) ||
            !bool.TryParse(args[5], out var option2))
            return true;

        try
        {
            ValidateTarget(target, scope);
            if (command.Equals("--maintenance-uninstall", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(CurrentExecutable, target, StringComparison.OrdinalIgnoreCase))
            {
                RelaunchUninstallFromTemporaryCopy(args);
                return true;
            }
            WaitForExit(parentPid);
            if (command.Equals("--maintenance-install", StringComparison.OrdinalIgnoreCase))
                InstallOrUpdate(target, scope, isUpdate: false, option1, option2);
            else if (command.Equals("--maintenance-update", StringComparison.OrdinalIgnoreCase))
                InstallOrUpdate(target, scope, isUpdate: true, false, false);
            else if (command.Equals("--maintenance-uninstall", StringComparison.OrdinalIgnoreCase))
                Uninstall(target, scope);
        }
        catch (Exception ex)
        {
            ThemedDialogWindow.ShowError(null, "Minimemizer maintenance failed", ex.Message);
        }
        return true;
    }

    private static void ValidateTarget(string target, InstallationScope scope)
    {
        if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(target).StartsWith("Minimemizer", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The target filename is invalid.");
        if (scope != InstallationScope.Portable &&
            !string.Equals(target, GetInstallPath(scope), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The installation target is invalid.");
    }

    private static void WaitForExit(int processId)
    {
        try { using var process = Process.GetProcessById(processId); process.WaitForExit(15000); }
        catch (ArgumentException) { }
    }

    private static void RelaunchUninstallFromTemporaryCopy(string[] originalArgs)
    {
        var helperDirectory = Path.Combine(Path.GetTempPath(), "Minimemizer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(helperDirectory);
        var helper = Path.Combine(helperDirectory, "Minimemizer-maintenance.exe");
        File.Copy(CurrentExecutable, helper, true);
        var info = new ProcessStartInfo(helper) { UseShellExecute = false };
        foreach (var argument in originalArgs) info.ArgumentList.Add(argument);
        info.ArgumentList[3] = Environment.ProcessId.ToString();
        Process.Start(info);
    }

    private static void InstallOrUpdate(string target, InstallationScope scope, bool isUpdate, bool startMenu, bool desktop)
    {
        EnsureNoOtherCopiesAreRunning();
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var source = CurrentExecutable;
        var backup = target + ".previous";
        if (isUpdate && File.Exists(target)) File.Copy(target, backup, true);
        try
        {
            File.Copy(source, target, true);
            if (scope != InstallationScope.Portable)
            {
                RegisterInstallation(target, scope);
                if (!isUpdate)
                {
                    if (startMenu) CreateShortcut(target, scope, desktop: false);
                    if (desktop) CreateShortcut(target, scope, desktop: true);
                }
            }
            var healthMarker = Path.Combine(Path.GetTempPath(), $"Minimemizer-health-{Guid.NewGuid():N}.ok");
            var launchInfo = scope == InstallationScope.AllUsers
                ? CreateUnelevatedLaunch(target, healthMarker, source)
                : new ProcessStartInfo(target) { UseShellExecute = true };
            if (scope != InstallationScope.AllUsers)
            {
                if (scope == InstallationScope.Portable) launchInfo.ArgumentList.Add("--portable");
                launchInfo.ArgumentList.Add("--maintenance-health");
                launchInfo.ArgumentList.Add(healthMarker);
                AddCleanupArguments(launchInfo, source);
            }
            using var launched = Process.Start(launchInfo);
            if (launched is null) throw new InvalidOperationException("The updated application could not be started.");
            var deadline = DateTime.UtcNow.AddSeconds(12);
            while (!File.Exists(healthMarker) && DateTime.UtcNow < deadline) Thread.Sleep(200);
            if (!File.Exists(healthMarker)) throw new InvalidOperationException("The updated application did not complete startup.");
            File.Delete(healthMarker);
            if (File.Exists(backup)) File.Delete(backup);
        }
        catch
        {
            if (File.Exists(backup))
            {
                try { File.Copy(backup, target, true); }
                catch (IOException) { MoveFileEx(backup, target, 5); }
                catch (UnauthorizedAccessException) { MoveFileEx(backup, target, 5); }
            }
            throw;
        }
    }

    private static void RegisterInstallation(string target, InstallationScope scope)
    {
        using var root = OpenRoot(scope, writable: true);
        using (var product = root.CreateSubKey(ProductKey))
        {
            product.SetValue("InstallPath", target);
            product.SetValue("Scope", scope.ToString());
        }
        using var uninstall = root.CreateSubKey(UninstallKey);
        var version = FileVersionInfo.GetVersionInfo(target).ProductVersion ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        uninstall.SetValue("DisplayName", "Minimemizer");
        uninstall.SetValue("DisplayVersion", version);
        uninstall.SetValue("Publisher", "Minimemizer");
        uninstall.SetValue("InstallLocation", Path.GetDirectoryName(target)!);
        uninstall.SetValue("DisplayIcon", target);
        uninstall.SetValue("UninstallString", $"\"{target}\" --request-uninstall");
        uninstall.SetValue("NoModify", 1, RegistryValueKind.DWord);
        uninstall.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (run?.GetValue("Minimemizer") is string) run.SetValue("Minimemizer", $"\"{target}\"");
    }

    private static ProcessStartInfo CreateUnelevatedLaunch(string target, string healthMarker, string source)
    {
        var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        info.ArgumentList.Add(target);
        info.ArgumentList.Add("--maintenance-health");
        info.ArgumentList.Add(healthMarker);
        AddCleanupArguments(info, source);
        return info;
    }

    private static void AddCleanupArguments(ProcessStartInfo info, string source)
    {
        var marker = $"{Path.DirectorySeparatorChar}Minimemizer{Path.DirectorySeparatorChar}";
        if (!source.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
            !(source.Contains($"{Path.DirectorySeparatorChar}Updates{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
              source.Contains($"{Path.DirectorySeparatorChar}Staging{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))) return;
        info.ArgumentList.Add("--maintenance-cleanup");
        info.ArgumentList.Add(source);
        info.ArgumentList.Add(Environment.ProcessId.ToString());
    }

    private static void Uninstall(string target, InstallationScope scope)
    {
        EnsureNoOtherCopiesAreRunning();
        RemoveShortcut(scope, desktop: false);
        RemoveShortcut(scope, desktop: true);
        using (var root = OpenRoot(scope, writable: true))
        {
            root.DeleteSubKeyTree(ProductKey, throwOnMissingSubKey: false);
            root.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        }
        using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
        {
            if (run?.GetValue("Minimemizer") is string value && value.Contains(target, StringComparison.OrdinalIgnoreCase))
                run.DeleteValue("Minimemizer", false);
        }
        if (File.Exists(target)) File.Delete(target);
        var directory = Path.GetDirectoryName(target)!;
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        MoveFileEx(CurrentExecutable, null, 4);
    }

    private static void DeleteCurrentUserData()
    {
        var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Minimemizer");
        if (Directory.Exists(settings)) Directory.Delete(settings, true);
        var state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Minimemizer");
        if (Directory.Exists(state)) Directory.Delete(state, true);
    }

    private static void EnsureNoOtherCopiesAreRunning()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            var found = false;
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    if (process.Id == Environment.ProcessId) continue;
                    try
                    {
                        if (process.ProcessName.StartsWith("Minimemizer", StringComparison.OrdinalIgnoreCase) && !process.HasExited)
                            found = true;
                    }
                    catch (System.ComponentModel.Win32Exception) { }
                    catch (InvalidOperationException) { }
                }
            }
            if (!found) return;
            if (DateTime.UtcNow >= deadline)
                throw new InvalidOperationException("Another Minimemizer process is still running, possibly in another user session.");
            Thread.Sleep(200);
        }
    }

    private static RegistryKey OpenRoot(InstallationScope scope, bool writable) =>
        scope == InstallationScope.AllUsers
            ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            : RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);

    private static string ShortcutPath(InstallationScope scope, bool desktop)
    {
        var folder = desktop
            ? Environment.GetFolderPath(scope == InstallationScope.AllUsers ? Environment.SpecialFolder.CommonDesktopDirectory : Environment.SpecialFolder.DesktopDirectory)
            : Path.Combine(Environment.GetFolderPath(scope == InstallationScope.AllUsers ? Environment.SpecialFolder.CommonStartMenu : Environment.SpecialFolder.StartMenu), "Programs");
        return Path.Combine(folder, "Minimemizer.lnk");
    }

    private static void CreateShortcut(string target, InstallationScope scope, bool desktop)
    {
        var path = ShortcutPath(scope, desktop);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows shortcut support is unavailable.");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic shortcut = shell.CreateShortcut(path);
        shortcut.TargetPath = target;
        shortcut.WorkingDirectory = Path.GetDirectoryName(target);
        shortcut.IconLocation = target;
        shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }

    private static void RemoveShortcut(InstallationScope scope, bool desktop)
    {
        var path = ShortcutPath(scope, desktop);
        if (File.Exists(path)) File.Delete(path);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, int flags);
}
