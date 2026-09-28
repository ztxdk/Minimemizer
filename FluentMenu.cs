using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Microsoft.Win32;

namespace Minimemizer;

internal static class AppTheme
{
    internal static bool IsDarkModeEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch { return false; }
    }
}

internal static class FluentMenu
{
    internal static ContextMenu Create(bool dark)
    {
        var surface = dark ? "#FF262626" : "#FFF9F9F9";
        var foreground = dark ? "#FFF2F2F2" : "#FF181818";
        var muted = dark ? "#FFAAAAAA" : "#FF666666";
        var border = dark ? "#FF505050" : "#FFC8C8C8";
        var hover = dark ? "#FF3A3A3A" : "#FFE8E8E8";
        var line = dark ? "#FF474747" : "#FFD8D8D8";

        var menu = new ContextMenu
        {
            StaysOpen = false,
            MaxHeight = Math.Max(160, SystemParameters.WorkArea.Height - 24)
        };
        menu.Style = (Style)XamlReader.Parse($$$"""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type ContextMenu}">
          <Setter Property="Background" Value="{{{surface}}}"/><Setter Property="Foreground" Value="{{{foreground}}}"/><Setter Property="BorderBrush" Value="{{{border}}}"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="4"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type ContextMenu}"><Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6" Padding="{TemplateBinding Padding}"><ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" MaxHeight="{TemplateBinding MaxHeight}"><StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/></ScrollViewer></Border></ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
        menu.Resources[typeof(MenuItem)] = XamlReader.Parse($$$"""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type MenuItem}">
          <Setter Property="Foreground" Value="{{{foreground}}}"/><Setter Property="Background" Value="Transparent"/><Setter Property="Padding" Value="12,8"/><Setter Property="MinWidth" Value="270"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type MenuItem}"><Border x:Name="Root" Background="{TemplateBinding Background}" CornerRadius="4" Padding="{TemplateBinding Padding}"><Grid><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions><ContentPresenter VerticalAlignment="Center" ContentSource="Header" RecognizesAccessKey="True"/><TextBlock x:Name="Check" Grid.Column="1" Text="✓" Margin="14,0,0,0" Foreground="{TemplateBinding Foreground}" FontWeight="SemiBold" Visibility="Collapsed"/></Grid></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Root" Property="Background" Value="{{{hover}}}"/></Trigger><Trigger Property="IsChecked" Value="True"><Setter TargetName="Check" Property="Visibility" Value="Visible"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Foreground" Value="{{{muted}}}"/><Setter TargetName="Root" Property="Opacity" Value=".8"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
        menu.Resources[typeof(Separator)] = XamlReader.Parse($$$"""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type Separator}"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type Separator}"><Border Height="1" Margin="8,4" Background="{{{line}}}"/></ControlTemplate></Setter.Value></Setter></Style>
        """);
        return menu;
    }
}
