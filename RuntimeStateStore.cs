using System.Text.Json;
using System.IO;

namespace Minimemizer;

internal sealed class RuntimeState
{
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    public List<string> PortablePromptDismissedPaths { get; set; } = [];
}

internal sealed class RuntimeStateStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Minimemizer", "state.json");

    internal RuntimeState Current { get; private set; } = new();

    internal void Load()
    {
        try
        {
            if (File.Exists(_path))
                Current = JsonSerializer.Deserialize<RuntimeState>(File.ReadAllText(_path)) ?? new();
        }
        catch { Current = new(); }
        Current.PortablePromptDismissedPaths ??= [];
    }

    internal void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal bool IsPortablePromptDismissed(string path) => Current.PortablePromptDismissedPaths
        .Any(item => string.Equals(Path.GetFullPath(item), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));

    internal void DismissPortablePrompt(string path)
    {
        if (!IsPortablePromptDismissed(path)) Current.PortablePromptDismissedPaths.Add(Path.GetFullPath(path));
        Save();
    }
}
