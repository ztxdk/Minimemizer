using System.Text.Json;
using System.IO;
using Microsoft.Win32;

namespace Minimemizer;

public sealed class SettingsStore
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Minimemizer", "settings.json");
    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(_path)) Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new();
        }
        catch { Current = new(); }
        Normalize();
    }

    public void Save()
    {
        Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        ApplyAutoStart();
    }

    private void Normalize()
    {
        Current.ThumbnailWidth = Math.Clamp(Current.ThumbnailWidth, 100, 800);
        Current.ThumbnailHeight = Math.Clamp(Current.ThumbnailHeight, 60, 600);
        Current.Gap = Math.Clamp(Current.Gap, 0, 100);
        Current.EdgeMargin = Math.Clamp(Current.EdgeMargin, 0, 200);
        Current.ThumbnailOpacity = Math.Clamp(Current.ThumbnailOpacity, 20, 100);
        Current.ExcludedPaths = Current.ExcludedPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
    }

    private void ApplyAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (Current.AutoStart)
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Programmets filsti kunne ikke findes.");
            key.SetValue("Minimemizer", $"\"{exe}\"");
        }
        else key.DeleteValue("Minimemizer", throwOnMissingValue: false);
    }
}
