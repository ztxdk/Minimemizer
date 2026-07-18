using System.Diagnostics;
using System.IO;
using System.Text;

namespace Minimemizer;

internal static class WindowTitleFormatter
{
    internal static string GetProgramDisplayName(string executablePath)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(executablePath);
            return FirstUseful(version.FileDescription, version.ProductName, Path.GetFileNameWithoutExtension(executablePath));
        }
        catch
        {
            return Path.GetFileNameWithoutExtension(executablePath);
        }
    }

    internal static string Shorten(string title, string executablePath)
    {
        var fallback = GetProgramDisplayName(executablePath);
        if (string.IsNullOrWhiteSpace(title)) return fallback;

        var trimmed = title.Trim();
        var separator = LastSeparator(trimmed);
        if (separator > 0 && separator < trimmed.Length - 1)
        {
            var suffix = trimmed[(separator + 1)..].Trim();
            if (LooksLikeProgramName(suffix, executablePath))
            {
                var shortened = trimmed[..separator].TrimEnd();
                if (!string.IsNullOrWhiteSpace(shortened)) return shortened;
            }
        }
        return trimmed;
    }

    private static bool LooksLikeProgramName(string suffix, string executablePath)
    {
        var candidates = new List<string> { Path.GetFileNameWithoutExtension(executablePath) };
        try
        {
            var version = FileVersionInfo.GetVersionInfo(executablePath);
            if (!string.IsNullOrWhiteSpace(version.FileDescription)) candidates.Add(version.FileDescription);
            if (!string.IsNullOrWhiteSpace(version.ProductName)) candidates.Add(version.ProductName);
        }
        catch { }

        var suffixWords = Words(suffix);
        if (suffixWords.Count == 0) return false;
        foreach (var candidate in candidates.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var candidateWords = Words(candidate);
            if (candidateWords.Count == 0) continue;
            var normalizedSuffix = string.Concat(suffixWords);
            var normalizedCandidate = string.Concat(candidateWords);
            if (normalizedSuffix.Equals(normalizedCandidate, StringComparison.OrdinalIgnoreCase) ||
                (normalizedSuffix.Length >= 4 && normalizedCandidate.Contains(normalizedSuffix, StringComparison.OrdinalIgnoreCase)) ||
                (normalizedCandidate.Length >= 4 && normalizedSuffix.Contains(normalizedCandidate, StringComparison.OrdinalIgnoreCase)) ||
                suffixWords.Intersect(candidateWords, StringComparer.OrdinalIgnoreCase).Any(word => word.Length >= 4))
                return true;
        }
        return false;
    }

    private static List<string> Words(string value)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character)) current.Append(char.ToLowerInvariant(character));
            else if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
        }
        if (current.Length > 0) words.Add(current.ToString());
        return words;
    }

    private static int LastSeparator(string value)
    {
        var result = -1;
        foreach (var separator in new[] { " - ", " – ", " — " })
            result = Math.Max(result, value.LastIndexOf(separator, StringComparison.Ordinal));
        return result < 0 ? -1 : result + 1;
    }

    private static string FirstUseful(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "Application";
}
