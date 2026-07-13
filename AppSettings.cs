namespace Minimemizer;

public enum ScreenCorner { TopLeft, TopRight, BottomLeft, BottomRight }
public enum ThumbnailFlow { Horizontal, Vertical }
public enum ThumbnailFrameStyle { None, Square, Rounded }
public enum ThumbnailSizeMode { Adaptive, Uniform }
public enum UniformContentMode { Crop, Contain }
public enum AppLanguage { Danish, English }
// Keep the original numeric values stable because enums are persisted as numbers in settings.json.
public enum ThumbnailIconPosition { TopLeft = 0, TopRight = 1, BottomLeft = 2, BottomRight = 3, TopCenter = 4, BottomCenter = 5 }

public sealed class AppSettings
{
    public int ThumbnailWidth { get; set; } = 240;
    public int ThumbnailHeight { get; set; } = 135;
    public int Gap { get; set; } = 12;
    public int EdgeMargin { get; set; } = 16;
    public string ScreenDeviceName { get; set; } = "";
    public ScreenCorner Corner { get; set; } = ScreenCorner.BottomRight;
    public ThumbnailFlow Flow { get; set; } = ThumbnailFlow.Horizontal;
    public bool AutoStart { get; set; }
    public bool ShowProgramIcon { get; set; } = true;
    public bool ShowTitleOnHover { get; set; } = true;
    public bool RestoreOnSingleClick { get; set; }
    public ThumbnailFrameStyle FrameStyle { get; set; } = ThumbnailFrameStyle.None;
    public ThumbnailIconPosition IconPosition { get; set; } = ThumbnailIconPosition.TopRight;
    public int ThumbnailOpacity { get; set; } = 100;
    public bool EnableThumbnailContextMenu { get; set; } = true;
    public ThumbnailSizeMode SizeMode { get; set; } = ThumbnailSizeMode.Adaptive;
    public UniformContentMode UniformContent { get; set; } = UniformContentMode.Crop;
    public AppLanguage Language { get; set; } = AppLanguage.English;
    public List<string> ExcludedPaths { get; set; } = [];
}
