using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Control = System.Windows.Controls.Control;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace Minimemizer;

internal sealed class ThemedDialogWindow : Window
{
    private readonly bool _dark = AppTheme.IsDarkModeEnabled();

    private ThemedDialogWindow(string heading, string message, string primaryText, string? secondaryText)
    {
        Title = "Minimemizer";
        Width = 490;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush(_dark ? 32 : 249, _dark ? 32 : 249, _dark ? 32 : 249);
        Foreground = Brush(_dark ? 242 : 24, _dark ? 242 : 24, _dark ? 242 : 24);
        SourceInitialized += (_, _) => ApplyNativeAppearance();

        var content = new Grid { Margin = new Thickness(28, 24, 28, 22) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(new TextBlock
        {
            Text = heading,
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Foreground = Foreground
        });
        var description = new TextBlock
        {
            Text = message,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Foreground,
            Opacity = .78,
            Margin = new Thickness(0, 9, 0, 26)
        };
        Grid.SetRow(description, 1);
        content.Children.Add(description);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (!string.IsNullOrWhiteSpace(secondaryText))
        {
            var secondary = CreateButton(secondaryText, primary: false);
            secondary.IsCancel = true;
            secondary.Margin = new Thickness(0, 0, 9, 0);
            secondary.Click += (_, _) => DialogResult = false;
            buttons.Children.Add(secondary);
        }
        var primary = CreateButton(primaryText, primary: true);
        primary.IsDefault = true;
        primary.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(primary);
        Grid.SetRow(buttons, 2);
        content.Children.Add(buttons);
        Content = content;
    }

    internal static bool ConfirmSingleInstance(AppLanguage language)
    {
        return Confirm(null,
            Localizer.T(language, "Minimemizer kører allerede."),
            Localizer.T(language, "Vil du lukke den kørende udgave og starte denne?"),
            Localizer.T(language, "Luk og start denne"),
            Localizer.T(language, "Annuller"));
    }

    internal static void ShowSingleInstanceError(AppLanguage language)
    {
        ShowError(null,
            Localizer.T(language, "Den kørende udgave kunne ikke lukkes."),
            Localizer.T(language, "Luk den kørende udgave manuelt, og prøv igen."));
    }

    internal static bool Confirm(Window? owner, string heading, string message, string primaryText, string secondaryText)
    {
        var dialog = new ThemedDialogWindow(heading, message, primaryText, secondaryText);
        if (owner?.IsVisible == true) dialog.Owner = owner;
        return dialog.ShowDialog() == true;
    }

    internal static void ShowError(Window? owner, string heading, string message)
    {
        var dialog = new ThemedDialogWindow(heading, message, "OK", null);
        if (owner?.IsVisible == true) dialog.Owner = owner;
        _ = dialog.ShowDialog();
    }

    private Button CreateButton(string text, bool primary)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = primary ? 150 : 100,
            Height = 36,
            Padding = new Thickness(15, 0, 15, 0),
            Foreground = primary ? Brushes.White : Foreground,
            Background = primary
                ? Brush(0, 103, 192)
                : Brush(_dark ? 51 : 247, _dark ? 51 : 247, _dark ? 51 : 247),
            BorderBrush = primary
                ? Brush(0, 103, 192)
                : Brush(_dark ? 85 : 200, _dark ? 85 : 200, _dark ? 85 : 200),
            BorderThickness = new Thickness(1)
        };
        button.Template = CreateButtonTemplate(_dark, primary);
        return button;
    }

    private static ControlTemplate CreateButtonTemplate(bool dark, bool primary)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Border";
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty,
            primary ? Brush(16, 116, 207) : Brush(dark ? 65 : 233, dark ? 65 : 233, dark ? 65 : 233)));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Control.OpacityProperty, .78));
        template.Triggers.Add(pressed);
        return template;
    }

    private void ApplyNativeAppearance()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var dark = _dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        var corner = 2;
        NativeMethods.DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int));
    }

    private static SolidColorBrush Brush(int red, int green, int blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb((byte)red, (byte)green, (byte)blue));
        brush.Freeze();
        return brush;
    }
}
