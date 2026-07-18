using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace Minimemizer;

internal static class FluentCard
{
    internal static Border Create(string title, string description, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(16, 12, 16, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        text.Children.Add(new TextBlock { Text = description, Opacity = .68, FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(text);
        control.SetValue(Grid.ColumnProperty, 1);
        if (control is FrameworkElement element) element.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(control);
        return new Border { CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 8), Child = grid, Tag = "card" };
    }
}

internal sealed class ThumbnailPreview : Border
{
    private readonly Border _thumbnail = new();
    private readonly Border _mockContent = new();
    private readonly Border _icon = new();
    private readonly Grid _canvas = new();
    private readonly Grid _thumbnailCanvas = new();
    private readonly Border _insideTitle = new();
    private readonly Border _aboveTitle = new();

    internal ThumbnailPreview()
    {
        Height = 235;
        CornerRadius = new CornerRadius(10);
        Margin = new Thickness(0, 0, 0, 12);
        Padding = new Thickness(14);
        Tag = "preview";
        _canvas.Children.Add(_thumbnail);
        Child = _canvas;

        var mock = new Grid();
        mock.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
        mock.RowDefinitions.Add(new RowDefinition());
        mock.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(42, 45, 52)) });
        var body = new Grid { Background = new SolidColorBrush(Color.FromRgb(30, 33, 39)) };
        body.Children.Add(new TextBlock { Text = "MINIMEMIZER", Foreground = new SolidColorBrush(Color.FromRgb(112, 174, 255)), FontSize = 18, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetRow(body, 1); mock.Children.Add(body);
        _mockContent.Child = mock;
        _thumbnailCanvas.Children.Add(_mockContent);
        _insideTitle.Child = new TextBlock
        {
            Text = "Pictures",
            Foreground = Brushes.White,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _insideTitle.Background = new SolidColorBrush(Color.FromArgb(225, 35, 35, 35));
        _insideTitle.BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 85));
        _insideTitle.BorderThickness = new Thickness(1);
        _insideTitle.CornerRadius = new CornerRadius(5);
        _insideTitle.Padding = new Thickness(7, 3, 7, 3);
        _insideTitle.Margin = new Thickness(7);
        _insideTitle.HorizontalAlignment = HorizontalAlignment.Left;
        _insideTitle.VerticalAlignment = VerticalAlignment.Bottom;
        _insideTitle.Visibility = Visibility.Collapsed;
        _thumbnailCanvas.Children.Add(_insideTitle);

        _aboveTitle.Child = new TextBlock
        {
            Text = "Pictures",
            Foreground = Brushes.White,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        _aboveTitle.Background = new SolidColorBrush(Color.FromRgb(38, 38, 38));
        _aboveTitle.BorderBrush = new SolidColorBrush(Color.FromRgb(82, 82, 82));
        _aboveTitle.BorderThickness = new Thickness(1, 1, 1, 0);
        _aboveTitle.Padding = new Thickness(8, 0, 8, 0);
        _aboveTitle.HorizontalAlignment = HorizontalAlignment.Stretch;
        _aboveTitle.VerticalAlignment = VerticalAlignment.Top;
        _aboveTitle.Visibility = Visibility.Collapsed;
        _thumbnailCanvas.Children.Add(_aboveTitle);
        _thumbnail.Child = _thumbnailCanvas;

        _icon.Width = _icon.Height = 34;
        _icon.CornerRadius = new CornerRadius(7);
        _icon.Background = new SolidColorBrush(Color.FromRgb(46, 119, 214));
        _icon.BorderBrush = Brushes.White;
        _icon.BorderThickness = new Thickness(1);
        _icon.Child = new TextBlock { Text = "M", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _thumbnailCanvas.Children.Add(_icon);
    }

    internal void Update(AppSettings settings)
    {
        const double maximumWidth = 390, maximumHeight = 195;
        var desiredWidth = settings.ThumbnailWidth;
        var previewHeight = settings.SizeMode == ThumbnailSizeMode.Adaptive
            ? Math.Min(settings.ThumbnailHeight, settings.ThumbnailWidth * .625)
            : settings.ThumbnailHeight;
        var titleBarHeight = settings.TitleMode == ThumbnailTitleMode.AlwaysAbove ? 28d : 0d;
        var desiredHeight = previewHeight + titleBarHeight;
        var scale = Math.Min(1d, Math.Min(maximumWidth / desiredWidth, maximumHeight / desiredHeight));
        _thumbnail.Width = Math.Max(40, desiredWidth * scale);
        _thumbnail.Height = Math.Max(30, desiredHeight * scale);
        _thumbnail.HorizontalAlignment = HorizontalAlignment.Center;
        _thumbnail.VerticalAlignment = VerticalAlignment.Center;
        _thumbnail.Opacity = settings.ThumbnailOpacity / 100d;
        _thumbnail.BorderThickness = settings.FrameStyle == ThumbnailFrameStyle.None ? new Thickness(0) : new Thickness(4);
        _thumbnail.BorderBrush = new SolidColorBrush(Color.FromRgb(92, 96, 104));
        _thumbnail.CornerRadius = settings.FrameStyle == ThumbnailFrameStyle.Rounded ? new CornerRadius(12) : new CornerRadius(0);
        _thumbnail.ClipToBounds = true;

        var scaledTitleHeight = titleBarHeight * scale;
        _aboveTitle.Visibility = settings.TitleMode == ThumbnailTitleMode.AlwaysAbove ? Visibility.Visible : Visibility.Collapsed;
        _aboveTitle.Height = scaledTitleHeight;
        _aboveTitle.CornerRadius = settings.FrameStyle == ThumbnailFrameStyle.Rounded ? new CornerRadius(9, 9, 0, 0) : new CornerRadius(0);
        _insideTitle.Visibility = settings.TitleMode == ThumbnailTitleMode.AlwaysInside ? Visibility.Visible : Visibility.Collapsed;
        _insideTitle.HorizontalAlignment = settings.ShowProgramIcon && settings.IconPosition is ThumbnailIconPosition.BottomLeft or ThumbnailIconPosition.BottomCenter
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;
        _mockContent.Margin = new Thickness(0, scaledTitleHeight, 0, 0);

        var innerWidth = Math.Max(1, _thumbnail.Width - _thumbnail.BorderThickness.Left * 2);
        var innerHeight = Math.Max(1, _thumbnail.Height - scaledTitleHeight - _thumbnail.BorderThickness.Top * 2);
        if (settings.SizeMode == ThumbnailSizeMode.Uniform && settings.UniformContent == UniformContentMode.Contain)
        {
            const double sourceAspect = 1.6;
            var targetAspect = innerWidth / innerHeight;
            _mockContent.Width = targetAspect > sourceAspect ? innerHeight * sourceAspect : innerWidth;
            _mockContent.Height = targetAspect > sourceAspect ? innerHeight : innerWidth / sourceAspect;
        }
        else
        {
            _mockContent.Width = double.NaN;
            _mockContent.Height = double.NaN;
        }
        _mockContent.HorizontalAlignment = HorizontalAlignment.Center;
        _mockContent.VerticalAlignment = VerticalAlignment.Center;
        _mockContent.ClipToBounds = true;
        _icon.Visibility = settings.ShowProgramIcon ? Visibility.Visible : Visibility.Collapsed;
        _icon.HorizontalAlignment = settings.IconPosition is ThumbnailIconPosition.TopLeft or ThumbnailIconPosition.BottomLeft ? HorizontalAlignment.Left : settings.IconPosition is ThumbnailIconPosition.TopCenter or ThumbnailIconPosition.BottomCenter ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        _icon.VerticalAlignment = settings.IconPosition is ThumbnailIconPosition.BottomLeft or ThumbnailIconPosition.BottomCenter or ThumbnailIconPosition.BottomRight ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        var iconAtTop = settings.IconPosition is ThumbnailIconPosition.TopLeft or ThumbnailIconPosition.TopCenter or ThumbnailIconPosition.TopRight;
        _icon.Margin = iconAtTop && scaledTitleHeight > 0
            ? new Thickness(7, 7 + scaledTitleHeight, 7, 7)
            : new Thickness(7);
    }
}
