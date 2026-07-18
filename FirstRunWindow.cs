using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Control = System.Windows.Controls.Control;
using RadioButton = System.Windows.Controls.RadioButton;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using MediaColor = System.Windows.Media.Color;

namespace Minimemizer;

internal sealed record FirstRunChoice(InstallationScope Scope, bool StartMenuShortcut, bool DesktopShortcut);

internal sealed class FirstRunWindow : Window
{
    private readonly RadioButton _currentUser = new() { IsChecked = true };
    private readonly RadioButton _allUsers = new();
    private readonly CheckBox _startMenu = new() { IsChecked = true };
    private readonly CheckBox _desktop = new();
    internal FirstRunChoice? Choice { get; private set; }

    internal FirstRunWindow(AppLanguage language)
    {
        string T(string value) => Localizer.T(language, value);
        Title = "Minimemizer";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(AppTheme.IsDarkModeEnabled() ? MediaColor.FromRgb(38, 38, 38) : MediaColor.FromRgb(249, 249, 249));
        Foreground = new SolidColorBrush(AppTheme.IsDarkModeEnabled() ? Colors.White : MediaColor.FromRgb(24, 24, 24));
        SourceInitialized += (_, _) => ApplyNativeAppearance();

        _currentUser.Content = T("Installér kun for mig");
        _allUsers.Content = T("Installér for alle brugere (kræver administrator)");
        _startMenu.Content = T("Opret genvej i Startmenuen");
        _desktop.Content = T("Opret genvej på skrivebordet");
        foreach (var control in new Control[] { _currentUser, _allUsers, _startMenu, _desktop }) control.Foreground = Foreground;

        var content = new StackPanel { Margin = new Thickness(28) };
        content.Children.Add(new TextBlock { Text = T("Velkommen til Minimemizer"), FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Foreground });
        content.Children.Add(new TextBlock { Text = T("Du kan installere programmet eller fortsætte som portable udgave."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 22), Foreground = Foreground, Opacity = .78 });
        content.Children.Add(_currentUser);
        _allUsers.Margin = new Thickness(0, 10, 0, 18); content.Children.Add(_allUsers);
        content.Children.Add(_startMenu);
        _desktop.Margin = new Thickness(0, 8, 0, 22); content.Children.Add(_desktop);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var portable = new Button { Content = T("Fortsæt portable"), Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) };
        var install = new Button { Content = T("Installér"), Padding = new Thickness(18, 7, 18, 7), IsDefault = true };
        portable.Click += (_, _) => { Choice = new FirstRunChoice(InstallationScope.Portable, false, false); DialogResult = true; };
        install.Click += (_, _) =>
        {
            Choice = new FirstRunChoice(_allUsers.IsChecked == true ? InstallationScope.AllUsers : InstallationScope.CurrentUser,
                _startMenu.IsChecked == true, _desktop.IsChecked == true);
            DialogResult = true;
        };
        buttons.Children.Add(portable); buttons.Children.Add(install); content.Children.Add(buttons);
        Content = content;
    }

    private void ApplyNativeAppearance()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var dark = AppTheme.IsDarkModeEnabled() ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        var corner = 2;
        NativeMethods.DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int));
    }
}
