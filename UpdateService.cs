using System.Net.Http;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Minimemizer;

internal enum UpdateStatus { Idle, Checking, UpToDate, Available, Downloading, Failed }

internal sealed record UpdateAsset(string Name, string DownloadUrl, long Size, string Digest);
internal sealed record AvailableUpdate(Version Version, string ReleaseUrl, UpdateAsset Asset);

internal sealed class UpdateService : IDisposable
{
    private const string LatestReleaseApi = "https://api.github.com/repos/ztxdk/Minimemizer/releases/latest";
    private readonly SettingsStore _settings;
    private readonly RuntimeStateStore _state;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private bool _busy;

    internal UpdateStatus Status { get; private set; }
    internal string? ErrorMessage { get; private set; }
    internal AvailableUpdate? Available { get; private set; }
    internal event EventHandler? StateChanged;
    internal event EventHandler<AvailableUpdate>? UpdateAvailable;

    internal UpdateService(SettingsStore settings, RuntimeStateStore state)
    {
        _settings = settings;
        _state = state;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Minimemizer/" + CurrentVersion);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    internal static Version CurrentVersion
    {
        get
        {
            var value = Assembly.GetExecutingAssembly().GetName().Version;
            return value is null ? new Version(0, 0, 0) : new Version(value.Major, value.Minor, Math.Max(0, value.Build));
        }
    }

    internal async Task CheckAutomaticallyAsync()
    {
        if (!_settings.Current.AutomaticUpdateChecks) return;
        if (_state.Current.LastUpdateCheckUtc is { } last && DateTimeOffset.UtcNow - last < TimeSpan.FromHours(24)) return;
        await Task.Delay(TimeSpan.FromSeconds(15));
        await CheckAsync(force: false);
    }

    internal async Task CheckAsync(bool force)
    {
        if (_busy) return;
        _busy = true;
        SetStatus(UpdateStatus.Checking);
        try
        {
            using var response = await _http.GetAsync(LatestReleaseApi);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new InvalidOperationException(Localizer.T(_settings.Current.Language,
                    "Opdateringskilden er ikke offentligt tilgængelig."));
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream) ?? throw new InvalidDataException("GitHub returned an empty release.");
            var versionText = release.TagName.Trim().TrimStart('v', 'V');
            if (!Version.TryParse(versionText, out var version)) throw new InvalidDataException("The release version is invalid.");
            var architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
            var suffix = $"-{architecture}-self-contained.exe";
            var asset = release.Assets.FirstOrDefault(item => item.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (version > CurrentVersion && asset is null)
                throw new InvalidDataException($"Release {version} does not contain a {architecture} self-contained build.");
            Available = version > CurrentVersion && asset is not null
                ? new AvailableUpdate(version, release.HtmlUrl,
                    new UpdateAsset(asset.Name, asset.BrowserDownloadUrl, asset.Size, asset.Digest ?? ""))
                : null;
            _state.Current.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
            _state.Save();
            ErrorMessage = null;
            SetStatus(Available is null ? UpdateStatus.UpToDate : UpdateStatus.Available);
            if (Available is not null) UpdateAvailable?.Invoke(this, Available);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            SetStatus(UpdateStatus.Failed);
        }
        finally { _busy = false; }
    }

    internal async Task<bool> DownloadAndStartUpdateAsync()
    {
        if (_busy || Available is null) return false;
        _busy = true;
        SetStatus(UpdateStatus.Downloading);
        try
        {
            if (!Available.Asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The release does not contain a SHA-256 digest.");
            var installation = InstallationManager.DetectCurrent();
            var updateRoot = installation.Scope == InstallationScope.AllUsers
                ? Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments)
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(updateRoot, "Minimemizer", "Updates", Available.Version.ToString(3));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, Available.Asset.Name);
            using (var response = await _http.GetAsync(Available.Asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var destination = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                await source.CopyToAsync(destination);
            }
            var info = new FileInfo(path);
            if (info.Length != Available.Asset.Size) throw new InvalidDataException("The downloaded file size does not match the release.");
            await using (var stream = File.OpenRead(path))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream));
                var expected = Available.Asset.Digest[7..].Trim();
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 verification failed.");
            }
            var started = InstallationManager.BeginUpdate(path, installation);
            if (!started) throw new InvalidOperationException("The updater could not be started.");
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            SetStatus(UpdateStatus.Failed);
            return false;
        }
        finally { _busy = false; }
    }

    private void SetStatus(UpdateStatus status)
    {
        Status = status;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _http.Dispose();

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
        [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("digest")] public string? Digest { get; set; }
    }
}
