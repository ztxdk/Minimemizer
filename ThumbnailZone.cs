using Forms = System.Windows.Forms;
using System.Text.RegularExpressions;

namespace Minimemizer;

internal readonly record struct ThumbnailZone(string ScreenDeviceName, ScreenCorner Corner);

internal static class ThumbnailZones
{
    internal static ThumbnailZone Default(AppSettings settings) =>
        new(settings.ScreenDeviceName ?? "", settings.Corner);

    internal static ThumbnailZone FromRule(ProgramZoneRule rule) =>
        new(rule.ScreenDeviceName ?? "", rule.Corner);

    internal static ThumbnailZone Resolve(ThumbnailZone requested, AppSettings settings, IReadOnlyList<Forms.Screen> screens)
    {
        var requestedScreen = screens.FirstOrDefault(screen =>
            string.Equals(screen.DeviceName, requested.ScreenDeviceName, StringComparison.OrdinalIgnoreCase));
        if (requestedScreen is not null) return new(requestedScreen.DeviceName, requested.Corner);

        var fallback = screens.FirstOrDefault(screen =>
                           string.Equals(screen.DeviceName, settings.ScreenDeviceName, StringComparison.OrdinalIgnoreCase))
                       ?? screens.FirstOrDefault(screen => screen.Primary)
                       ?? screens.First();
        return new(fallback.DeviceName, requested.Corner);
    }

    internal static Forms.Screen FindScreen(ThumbnailZone zone, IReadOnlyList<Forms.Screen> screens) =>
        screens.First(screen => string.Equals(screen.DeviceName, zone.ScreenDeviceName, StringComparison.OrdinalIgnoreCase));

    internal static string CornerLabel(AppLanguage language, ScreenCorner corner) => corner switch
    {
        ScreenCorner.TopLeft => Localizer.T(language, "Øverst til venstre"),
        ScreenCorner.TopRight => Localizer.T(language, "Øverst til højre"),
        ScreenCorner.BottomLeft => Localizer.T(language, "Nederst til venstre"),
        _ => Localizer.T(language, "Nederst til højre")
    };

    internal static string DisplayLabel(AppLanguage language, string deviceName, IReadOnlyList<Forms.Screen> screens)
    {
        var match = Regex.Match(deviceName ?? "", @"DISPLAY(?<number>\d+)$", RegexOptions.IgnoreCase);
        var number = match.Success
            ? match.Groups["number"].Value
            : Math.Max(1, screens.Select((screen, index) => (screen, index))
                .FirstOrDefault(item => string.Equals(item.screen.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)).index + 1).ToString();
        return string.Format(Localizer.T(language, "Skærm {0}"), number);
    }
}
