using System.Text.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using Slider = System.Windows.Controls.Slider;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using ListBox = System.Windows.Controls.ListBox;
using Panel = System.Windows.Controls.Panel;
using Orientation = System.Windows.Controls.Orientation;
using MessageBox = System.Windows.MessageBox;
using Control = System.Windows.Controls.Control;
using SystemColors = System.Windows.SystemColors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Color = System.Windows.Media.Color;

namespace Minimemizer;

public sealed class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly WindowManager _manager;
    private readonly AppSettings _draft;
    private readonly Dictionary<string, Button> _navigation = [];
    private readonly Grid _pageHost = new();
    private readonly TextBlock _pageTitle = new() { FontSize = 28, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _pageDescription = new() { FontSize = 13, Opacity = .7, Margin = new Thickness(0, 4, 0, 18) };
    private ThumbnailPreview _preview = new();
    private ListBox _exclusions = new();
    private ComboBox _contentMode = new(), _iconPosition = new();
    private bool _isDark;
    public event EventHandler? LanguageChanged;
    private AppLanguage UiLanguage => _draft.Language;
    private string T(string danish) => Localizer.T(UiLanguage, danish);

    public SettingsWindow(SettingsStore store, WindowManager manager)
    {
        _store = store;
        _manager = manager;
        _draft = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(store.Current)) ?? new AppSettings();
        Title = $"Minimemizer – {T("Indstillinger")}";
        Width = 920; Height = 680; MinWidth = 780; MinHeight = 570;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = BuildShell();
        ApplySystemTheme();
        ShowPage("general");
        Loaded += (_, _) => ApplySystemTheme();
        SourceInitialized += (_, _) => ApplyNativeAppearance();
        SystemEvents.UserPreferenceChanged += SystemThemeChanged;
        Closed += (_, _) => SystemEvents.UserPreferenceChanged -= SystemThemeChanged;
    }

    private UIElement BuildShell()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) });

        var navigation = new StackPanel { Margin = new Thickness(12, 22, 12, 12) };
        navigation.Children.Add(new TextBlock { Text = "Minimemizer", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 0, 0, 24) });
        AddNavigation(navigation, "general", "⚙", T("Generelt"));
        AddNavigation(navigation, "appearance", "◫", T("Udseende"));
        AddNavigation(navigation, "placement", "⌖", T("Placering"));
        AddNavigation(navigation, "programs", "▦", T("Programmer"));
        AddNavigation(navigation, "about", "ⓘ", T("Om"));
        Grid.SetRowSpan(navigation, 2); root.Children.Add(navigation);

        var content = new Grid { Margin = new Thickness(28, 24, 28, 12) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition());
        var heading = new StackPanel(); heading.Children.Add(_pageTitle); heading.Children.Add(_pageDescription);
        content.Children.Add(heading); Grid.SetRow(_pageHost, 1); content.Children.Add(_pageHost);
        Grid.SetColumn(content, 1); root.Children.Add(content);

        var footer = new Border { Tag = "footer", Padding = new Thickness(20, 12, 28, 12) };
        var footerButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = MakeButton(T("Annuller"), false); cancel.Click += (_, _) => Close();
        var apply = MakeButton(T("Anvend"), false); apply.Margin = new Thickness(8, 0, 0, 0); apply.Click += Apply;
        var save = MakeButton(T("Gem"), true); save.Margin = new Thickness(8, 0, 0, 0); save.Click += Save;
        footerButtons.Children.Add(cancel); footerButtons.Children.Add(apply); footerButtons.Children.Add(save); footer.Child = footerButtons;
        Grid.SetColumn(footer, 1); Grid.SetRow(footer, 1); root.Children.Add(footer);
        return root;
    }

    private void AddNavigation(Panel panel, string key, string glyph, string label)
    {
        var button = new Button { Content = $"{glyph}   {label}", Tag = "nav", HorizontalContentAlignment = HorizontalAlignment.Left, Height = 44, Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(14, 0, 8, 0) };
        button.Click += (_, _) => ShowPage(key); _navigation[key] = button; panel.Children.Add(button);
    }

    private void ShowPage(string key)
    {
        foreach (var pair in _navigation) pair.Value.SetValue(Control.FontWeightProperty, pair.Key == key ? FontWeights.SemiBold : FontWeights.Normal);
        _pageHost.Children.Clear();
        var (title, description, page) = key switch
        {
            "appearance" => (T("Udseende"), T("Tilpas thumbnails, rammer, ikoner og gennemsigtighed."), BuildAppearancePage()),
            "placement" => (T("Placering"), T("Vælg skærm, retning og afstande."), BuildPlacementPage()),
            "programs" => (T("Programmer"), T("Administrer programmer, der ikke skal have thumbnails."), BuildProgramsPage()),
            "about" => (T("Om"), T("Information om Minimemizer og denne udgave."), BuildAboutPage()),
            _ => (T("Generelt"), T("Grundlæggende funktioner og programmets sprog."), BuildGeneralPage())
        };
        _pageTitle.Text = title; _pageDescription.Text = description; _pageHost.Children.Add(page);
        Dispatcher.BeginInvoke(ApplySystemTheme);
    }

    private UIElement BuildGeneralPage()
    {
        var panel = PagePanel();
        panel.Children.Add(FluentCard.Create(T("Sprog"), T("Vælg sproget i Minimemizer."), Choice(new[] { (AppLanguage.Danish, T("Dansk")), (AppLanguage.English, T("Engelsk")) }, _draft.Language, value => _draft.Language = value)));
        panel.Children.Add(FluentCard.Create(T("Start med Windows"), T("Start automatisk, når du logger ind."), Toggle(_draft.AutoStart, value => _draft.AutoStart = value)));
        panel.Children.Add(FluentCard.Create(T("Åbn thumbnail"), T("Vælg om et program gendannes med enkelt- eller dobbeltklik."), Choice(new[] { (false, T("Dobbeltklik")), (true, T("Enkeltklik")) }, _draft.RestoreOnSingleClick, value => _draft.RestoreOnSingleClick = value)));
        panel.Children.Add(FluentCard.Create(T("Højrekliksmenu"), T("Vis programmets klassiske vinduesmenu."), Toggle(_draft.EnableThumbnailContextMenu, value => _draft.EnableThumbnailContextMenu = value)));
        return Scroll(panel);
    }

    private UIElement BuildAppearancePage()
    {
        _preview = new ThumbnailPreview(); _contentMode = new ComboBox(); _iconPosition = new ComboBox();
        var panel = PagePanel(); _preview.Update(_draft); panel.Children.Add(_preview);
        var dimensions = new StackPanel { Orientation = Orientation.Horizontal };
        dimensions.Children.Add(NumberBox(_draft.ThumbnailWidth, value => { _draft.ThumbnailWidth = value; Preview(); }));
        dimensions.Children.Add(new TextBlock { Text = "×", Margin = new Thickness(8, 5, 8, 0) });
        dimensions.Children.Add(NumberBox(_draft.ThumbnailHeight, value => { _draft.ThumbnailHeight = value; Preview(); }));
        panel.Children.Add(FluentCard.Create(T("Thumbnailstørrelse"), T("Maksimal bredde og højde i pixels."), dimensions));
        var sizeMode = Choice(new[] { (ThumbnailSizeMode.Adaptive, T("Adaptiv")), (ThumbnailSizeMode.Uniform, T("Ens størrelse")) }, _draft.SizeMode, value => { _draft.SizeMode = value; _contentMode.IsEnabled = value == ThumbnailSizeMode.Uniform; Preview(); });
        panel.Children.Add(FluentCard.Create(T("Størrelsestil"), T("Bevar vinduets format eller brug ens størrelse."), sizeMode));
        ConfigureChoice(_contentMode, new[] { (UniformContentMode.Crop, T("Beskær til rammen")), (UniformContentMode.Contain, T("Vis hele vinduet")) }, _draft.UniformContent, value => { _draft.UniformContent = value; Preview(); });
        _contentMode.IsEnabled = _draft.SizeMode == ThumbnailSizeMode.Uniform;
        panel.Children.Add(FluentCard.Create(T("Indholdstilpasning"), T("Bestem hvordan vinduet tilpasses en ens ramme."), _contentMode));
        panel.Children.Add(FluentCard.Create(T("Ramme"), T("Vælg ingen, kantet eller afrundet ramme."), Choice(new[] { (ThumbnailFrameStyle.None, T("Ingen ramme")), (ThumbnailFrameStyle.Square, T("Skarpe hjørner")), (ThumbnailFrameStyle.Rounded, T("Runde hjørner")) }, _draft.FrameStyle, value => { _draft.FrameStyle = value; Preview(); })));
        var opacity = new Slider { Minimum = 20, Maximum = 100, Value = _draft.ThumbnailOpacity, Width = 190, TickFrequency = 5, IsSnapToTickEnabled = true };
        opacity.ValueChanged += (_, _) => { _draft.ThumbnailOpacity = (int)opacity.Value; Preview(); };
        panel.Children.Add(FluentCard.Create(T("Gennemsigtighed"), T("Juster thumbnailens synlighed."), opacity));
        panel.Children.Add(FluentCard.Create(T("Programikon"), T("Vis programmets ikon oven på thumbnailen."), Toggle(_draft.ShowProgramIcon, value => { _draft.ShowProgramIcon = value; _iconPosition.IsEnabled = value; Preview(); })));
        ConfigureChoice(_iconPosition, IconChoices(), _draft.IconPosition, value => { _draft.IconPosition = value; Preview(); }); _iconPosition.IsEnabled = _draft.ShowProgramIcon;
        panel.Children.Add(FluentCard.Create(T("Ikonplacering"), T("Vælg ikonets placering på thumbnailen."), _iconPosition));
        panel.Children.Add(FluentCard.Create(T("Titel ved hover"), T("Vis programmets titel, når musen holdes over thumbnailen."), Toggle(_draft.ShowTitleOnHover, value => _draft.ShowTitleOnHover = value)));
        return Scroll(panel);
    }

    private UIElement BuildPlacementPage()
    {
        var panel = PagePanel();
        var screens = Forms.Screen.AllScreens.Select(s => (s.DeviceName, $"{s.DeviceName} ({s.Bounds.Width} × {s.Bounds.Height})")).ToArray();
        var selectedScreen = screens.Any(x => x.DeviceName == _draft.ScreenDeviceName) ? _draft.ScreenDeviceName : screens.FirstOrDefault().DeviceName ?? "";
        panel.Children.Add(FluentCard.Create(T("Skærm"), T("Skærmen hvor thumbnails skal placeres."), Choice(screens, selectedScreen, value => _draft.ScreenDeviceName = value)));
        panel.Children.Add(FluentCard.Create(T("Hjørne"), T("Startpunktet for rækken af thumbnails."), Choice(CornerChoices(), _draft.Corner, value => _draft.Corner = value)));
        panel.Children.Add(FluentCard.Create(T("Retning"), T("Placér thumbnails vandret eller lodret."), Choice(new[] { (ThumbnailFlow.Horizontal, T("Vandret")), (ThumbnailFlow.Vertical, T("Lodret")) }, _draft.Flow, value => _draft.Flow = value)));
        panel.Children.Add(FluentCard.Create(T("Afstand"), T("Afstand mellem thumbnails i pixels."), NumberBox(_draft.Gap, value => _draft.Gap = value)));
        panel.Children.Add(FluentCard.Create(T("Kantafstand"), T("Afstand fra skærmens kant i pixels."), NumberBox(_draft.EdgeMargin, value => _draft.EdgeMargin = value)));
        return Scroll(panel);
    }

    private UIElement BuildProgramsPage()
    {
        _exclusions = new ListBox();
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _exclusions.ItemsSource = _draft.ExcludedPaths.Order(StringComparer.OrdinalIgnoreCase).ToArray(); _exclusions.Margin = new Thickness(0, 0, 0, 12);
        grid.Children.Add(_exclusions);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var add = MakeButton(T("Tilføj program…"), false); add.Click += AddExclusion;
        var remove = MakeButton(T("Fjern valgte"), false); remove.Margin = new Thickness(8, 0, 0, 0); remove.Click += (_, _) => { if (_exclusions.SelectedItem is string item) { _draft.ExcludedPaths.Remove(item); RefreshExclusions(); } };
        buttons.Children.Add(add); buttons.Children.Add(remove); Grid.SetRow(buttons, 1); grid.Children.Add(buttons);
        return grid;
    }

    private UIElement BuildAboutPage()
    {
        var panel = PagePanel();
        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
        var version = assemblyVersion is null ? "0.5.8" : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
        panel.Children.Add(FluentCard.Create(T("Version"), T("Den installerede version af Minimemizer."), ValueText(version)));
        panel.Children.Add(FluentCard.Create(T("Arkitektur"), T("Den processorarkitektur denne udgave er bygget til."), ValueText(ArchitectureName(RuntimeInformation.ProcessArchitecture))));
        panel.Children.Add(FluentCard.Create(T("System"), T("Den Windows-arkitektur programmet kører på."), ValueText(ArchitectureName(RuntimeInformation.OSArchitecture))));
        panel.Children.Add(FluentCard.Create(T("Flere oplysninger"), T("Der kan tilføjes support-, licens- og weboplysninger her senere."), ValueText("—")));
        return Scroll(panel);
    }

    private static TextBlock ValueText(string value) => new() { Text = value, FontSize = 15, FontWeight = FontWeights.SemiBold, MinWidth = 110, TextAlignment = TextAlignment.Right };

    private static string ArchitectureName(Architecture architecture) => architecture switch
    {
        Architecture.Arm64 => "ARM64",
        Architecture.Arm => "ARM",
        Architecture.X64 => "x64",
        Architecture.X86 => "x86",
        _ => architecture.ToString()
    };

    private static StackPanel PagePanel() => new();
    private static ScrollViewer Scroll(UIElement child) => new() { Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 8, 8) };
    private void Preview() => _preview.Update(_draft);

    private CheckBox Toggle(bool value, Action<bool> changed)
    {
        var toggle = new CheckBox { IsChecked = value, Tag = "toggle", VerticalAlignment = VerticalAlignment.Center };
        toggle.Checked += (_, _) => changed(true); toggle.Unchecked += (_, _) => changed(false); return toggle;
    }

    private TextBox NumberBox(int value, Action<int> changed)
    {
        var box = new TextBox { Text = value.ToString(), Width = 76, Padding = new Thickness(8, 5, 8, 5) };
        box.TextChanged += (_, _) => { if (int.TryParse(box.Text, out var parsed)) changed(parsed); }; return box;
    }

    private ComboBox Choice<T>(IEnumerable<(T Value, string Label)> choices, T selected, Action<T> changed)
    {
        var combo = new ComboBox(); ConfigureChoice(combo, choices, selected, changed); return combo;
    }

    private static void ConfigureChoice<T>(ComboBox combo, IEnumerable<(T Value, string Label)> choices, T selected, Action<T> changed)
    {
        var items = choices.Select(x => new ChoiceItem<T>(x.Value, x.Label)).ToArray(); combo.ItemsSource = items; combo.Width = 210;
        combo.SelectedItem = items.FirstOrDefault(x => EqualityComparer<T>.Default.Equals(x.Value, selected)) ?? items.FirstOrDefault();
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ChoiceItem<T> item) changed(item.Value); };
    }

    private IEnumerable<(ThumbnailIconPosition, string)> IconChoices() => new[] { (ThumbnailIconPosition.TopLeft, T("Øverst til venstre")), (ThumbnailIconPosition.TopCenter, T("Øverst i midten")), (ThumbnailIconPosition.TopRight, T("Øverst til højre")), (ThumbnailIconPosition.BottomLeft, T("Nederst til venstre")), (ThumbnailIconPosition.BottomCenter, T("Nederst i midten")), (ThumbnailIconPosition.BottomRight, T("Nederst til højre")) };
    private IEnumerable<(ScreenCorner, string)> CornerChoices() => new[] { (ScreenCorner.TopLeft, T("Øverst til venstre")), (ScreenCorner.TopRight, T("Øverst til højre")), (ScreenCorner.BottomLeft, T("Nederst til venstre")), (ScreenCorner.BottomRight, T("Nederst til højre")) };

    private Button MakeButton(string text, bool primary) => new() { Content = text, Tag = primary ? "primary" : "button", MinWidth = 100, Height = 36, Padding = new Thickness(16, 0, 16, 0) };

    private void AddExclusion(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.OpenFileDialog { Filter = T("Programmer (*.exe)|*.exe"), Title = T("Vælg et program") };
        if (dialog.ShowDialog() == Forms.DialogResult.OK && !_draft.ExcludedPaths.Contains(dialog.FileName, StringComparer.OrdinalIgnoreCase)) { _draft.ExcludedPaths.Add(dialog.FileName); RefreshExclusions(); }
    }

    private void RefreshExclusions() { _exclusions.ItemsSource = null; _exclusions.ItemsSource = _draft.ExcludedPaths.Order(StringComparer.OrdinalIgnoreCase).ToArray(); }

    private void Apply(object sender, RoutedEventArgs e) => PersistChanges(closeAfterSave: false);

    private void Save(object sender, RoutedEventArgs e) => PersistChanges(closeAfterSave: true);

    private void PersistChanges(bool closeAfterSave)
    {
        _draft.ThumbnailWidth = Math.Clamp(_draft.ThumbnailWidth, 100, 800); _draft.ThumbnailHeight = Math.Clamp(_draft.ThumbnailHeight, 60, 600);
        _draft.Gap = Math.Clamp(_draft.Gap, 0, 100); _draft.EdgeMargin = Math.Clamp(_draft.EdgeMargin, 0, 200);
        try
        {
            var target = _store.Current;
            foreach (var property in typeof(AppSettings).GetProperties().Where(p => p.CanRead && p.CanWrite)) property.SetValue(target, property.GetValue(_draft));
            _store.Save();
            _manager.Refresh();
            LanguageChanged?.Invoke(this, EventArgs.Empty);
            if (closeAfterSave) Close();
        }
        catch (Exception ex) { MessageBox.Show(this, $"{T("Indstillingerne kunne ikke gemmes:")}\n{ex.Message}", "Minimemizer", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void SystemThemeChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(ApplySystemTheme);

    private void ApplySystemTheme()
    {
        _isDark = IsDarkModeEnabled();
        var background = Brush(_isDark ? 32 : 243, _isDark ? 32 : 243, _isDark ? 32 : 243);
        var surface = Brush(_isDark ? 45 : 255, _isDark ? 45 : 255, _isDark ? 45 : 255);
        var foreground = Brush(_isDark ? 242 : 24, _isDark ? 242 : 24, _isDark ? 242 : 24);
        var border = Brush(_isDark ? 72 : 215, _isDark ? 72 : 215, _isDark ? 72 : 215);
        Background = background; Foreground = foreground;
        Resources[SystemColors.WindowBrushKey] = surface; Resources[SystemColors.ControlTextBrushKey] = foreground;
        Resources[typeof(TextBlock)] = StyleOf(typeof(TextBlock), (TextBlock.ForegroundProperty, foreground));
        Resources[typeof(TextBox)] = StyleOf(typeof(TextBox), (TextBox.BackgroundProperty, surface), (TextBox.ForegroundProperty, foreground), (TextBox.BorderBrushProperty, border));
        Resources[typeof(ListBox)] = StyleOf(typeof(ListBox), (ListBox.BackgroundProperty, surface), (ListBox.ForegroundProperty, foreground), (ListBox.BorderBrushProperty, border));
        Resources[typeof(ComboBox)] = ComboStyle(_isDark);
        Resources[typeof(Button)] = ButtonStyle(_isDark);
        Resources[typeof(ScrollBar)] = ScrollBarStyle(_isDark);
        Resources[typeof(CheckBox)] = StyleOf(typeof(CheckBox), (CheckBox.ForegroundProperty, foreground));
        foreach (var borderElement in FindVisualChildren<Border>(this))
        {
            if (Equals(borderElement.Tag, "card")) { borderElement.Background = surface; borderElement.BorderBrush = border; borderElement.BorderThickness = new Thickness(1); }
            else if (Equals(borderElement.Tag, "preview")) { borderElement.Background = Brush(_isDark ? 24 : 232, _isDark ? 24 : 232, _isDark ? 24 : 232); borderElement.BorderBrush = border; borderElement.BorderThickness = new Thickness(1); }
            else if (Equals(borderElement.Tag, "footer")) { borderElement.Background = background; borderElement.BorderBrush = border; borderElement.BorderThickness = new Thickness(0, 1, 0, 0); }
        }
        ApplyNativeAppearance();
    }

    private void ApplyNativeAppearance()
    {
        var handle = new WindowInteropHelper(this).Handle; if (handle == 0) return;
        var dark = _isDark ? 1 : 0; NativeMethods.DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        var corner = 2; NativeMethods.DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int));
        var backdrop = 2; NativeMethods.DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int));
    }

    private static Style StyleOf(Type type, params (DependencyProperty Property, object Value)[] setters) { var style = new Style(type); foreach (var setter in setters) style.Setters.Add(new Setter(setter.Property, setter.Value)); return style; }
    private static SolidColorBrush Brush(int r, int g, int b) { var value = new SolidColorBrush(Color.FromRgb((byte)r, (byte)g, (byte)b)); value.Freeze(); return value; }
    private static bool IsDarkModeEnabled() { try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); return key?.GetValue("AppsUseLightTheme") is int value && value == 0; } catch { return false; } }
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T typed) yield return typed; foreach (var nested in FindVisualChildren<T>(child)) yield return nested; } }

    private static Style ComboStyle(bool dark)
    {
        var bg = dark ? "#FF2D2D2D" : "#FFFFFFFF"; var hover = dark ? "#FF3A3A3A" : "#FFF0F0F0"; var fg = dark ? "#FFF2F2F2" : "#FF181818"; var line = dark ? "#FF555555" : "#FFD0D0D0";
        return (Style)XamlReader.Parse($$$"""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type ComboBox}">
          <Setter Property="Background" Value="{{{bg}}}"/><Setter Property="Foreground" Value="{{{fg}}}"/><Setter Property="BorderBrush" Value="{{{line}}}"/><Setter Property="MinHeight" Value="34"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type ComboBox}"><Grid>
            <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="4"/>
            <ContentPresenter Margin="10,0,30,0" VerticalAlignment="Center" Content="{TemplateBinding SelectionBoxItem}" TextElement.Foreground="{TemplateBinding Foreground}"/>
            <ToggleButton Width="30" HorizontalAlignment="Right" Focusable="False" Background="Transparent" BorderThickness="0" IsChecked="{Binding IsDropDownOpen,RelativeSource={RelativeSource TemplatedParent},Mode=TwoWay}"><Path Width="8" Height="5" Fill="{TemplateBinding Foreground}" Data="M0,0 L4,5 L8,0 Z"/></ToggleButton>
            <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True"><Border MinWidth="{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}" MaxHeight="300" Background="{{{bg}}}" BorderBrush="{{{line}}}" BorderThickness="1"><ScrollViewer><ItemsPresenter/></ScrollViewer></Border></Popup>
          </Grid></ControlTemplate></Setter.Value></Setter>
          <Style.Resources><Style TargetType="{x:Type ComboBoxItem}"><Setter Property="Foreground" Value="{{{fg}}}"/><Setter Property="Background" Value="{{{bg}}}"/><Setter Property="Padding" Value="10,7"/><Style.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter Property="Background" Value="{{{hover}}}"/></Trigger></Style.Triggers></Style></Style.Resources>
        </Style>
        """);
    }

    private static Style ButtonStyle(bool dark)
    {
        var bg = dark ? "#FF333333" : "#FFF7F7F7"; var hover = dark ? "#FF414141" : "#FFE9E9E9"; var fg = dark ? "#FFF2F2F2" : "#FF181818"; var line = dark ? "#FF555555" : "#FFC8C8C8";
        return (Style)XamlReader.Parse($$$"""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type Button}">
          <Setter Property="Background" Value="{{{bg}}}"/><Setter Property="Foreground" Value="{{{fg}}}"/><Setter Property="BorderBrush" Value="{{{line}}}"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type Button}"><Border x:Name="Border" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="5" Padding="{TemplateBinding Padding}"><ContentPresenter VerticalAlignment="Center" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Border" Property="Background" Value="{{{hover}}}"/></Trigger><Trigger Property="IsPressed" Value="True"><Setter TargetName="Border" Property="Opacity" Value=".75"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="Border" Property="Opacity" Value=".45"/></Trigger><Trigger Property="Tag" Value="primary"><Setter TargetName="Border" Property="Background" Value="#FF0067C0"/><Setter Property="Foreground" Value="White"/></Trigger><Trigger Property="Tag" Value="nav"><Setter TargetName="Border" Property="BorderThickness" Value="0"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
    }

    private static Style ScrollBarStyle(bool dark)
    {
        var track = dark ? "#18FFFFFF" : "#14000000";
        var thumb = dark ? "#FF777777" : "#FF8A8A8A";
        var hover = dark ? "#FF999999" : "#FF686868";
        return (Style)XamlReader.Parse($$$"""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type ScrollBar}">
          <Setter Property="Background" Value="Transparent"/><Setter Property="Width" Value="12"/><Setter Property="MinWidth" Value="12"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type ScrollBar}">
            <Grid Background="Transparent" Margin="2,0">
              <Border Background="{{{track}}}" CornerRadius="4"/>
              <Track x:Name="PART_Track" IsDirectionReversed="True">
                <Track.DecreaseRepeatButton><RepeatButton Command="{x:Static ScrollBar.PageUpCommand}" Background="Transparent" BorderThickness="0" Focusable="False"/></Track.DecreaseRepeatButton>
                <Track.Thumb><Thumb MinHeight="28"><Thumb.Template><ControlTemplate TargetType="{x:Type Thumb}"><Border x:Name="Thumb" Background="{{{thumb}}}" CornerRadius="4"/><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Thumb" Property="Background" Value="{{{hover}}}"/></Trigger><Trigger Property="IsDragging" Value="True"><Setter TargetName="Thumb" Property="Background" Value="{{{hover}}}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                <Track.IncreaseRepeatButton><RepeatButton Command="{x:Static ScrollBar.PageDownCommand}" Background="Transparent" BorderThickness="0" Focusable="False"/></Track.IncreaseRepeatButton>
              </Track>
            </Grid>
          </ControlTemplate></Setter.Value></Setter>
          <Style.Triggers><Trigger Property="Orientation" Value="Horizontal"><Setter Property="Height" Value="12"/><Setter Property="MinHeight" Value="12"/><Setter Property="Width" Value="Auto"/><Setter Property="MinWidth" Value="0"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type ScrollBar}">
            <Grid Background="Transparent" Margin="0,2"><Border Background="{{{track}}}" CornerRadius="4"/><Track x:Name="PART_Track"><Track.DecreaseRepeatButton><RepeatButton Command="{x:Static ScrollBar.PageLeftCommand}" Background="Transparent" BorderThickness="0"/></Track.DecreaseRepeatButton><Track.Thumb><Thumb MinWidth="28"><Thumb.Template><ControlTemplate TargetType="{x:Type Thumb}"><Border x:Name="Thumb" Background="{{{thumb}}}" CornerRadius="4"/><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Thumb" Property="Background" Value="{{{hover}}}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command="{x:Static ScrollBar.PageRightCommand}" Background="Transparent" BorderThickness="0"/></Track.IncreaseRepeatButton></Track></Grid>
          </ControlTemplate></Setter.Value></Setter></Trigger></Style.Triggers>
        </Style>
        """);
    }

    private sealed record ChoiceItem<T>(T Value, string Label) { public override string ToString() => Label; }
}
