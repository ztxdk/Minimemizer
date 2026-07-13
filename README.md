<p align="center">
  <img src="assets/minimemizer-icon.png" alt="Minimemizer icon" width="160" height="160">
</p>

<h1 align="center">Minimemizer</h1>

<p align="center">
  Turn minimized Windows applications into customizable desktop thumbnails.
</p>

Minimemizer is a Windows 11 utility that displays minimized applications as live thumbnails on the desktop. Click a thumbnail to quickly restore its application.

The application icon is embedded in all official ARM64 and x64 builds and is also used in the system tray.

## Getting started

1. Download the correct build from the [latest release](https://github.com/ztxdk/Minimemizer/releases/latest).
2. Start `Minimemizer.exe`.
3. Minimize a regular application.
4. A thumbnail of the application appears on the desktop.
5. Single- or double-click the thumbnail to restore the application.

Minimemizer runs in the background and appears in the system tray next to the Windows clock. Right-click the tray icon to open **Settings** or exit the application.

> If the tray icon is not visible, it may be located under **Show hidden icons**.

## Choosing a download

| Build | Recommended for | .NET requirement |
|---|---|---|
| `win-x64-self-contained` | Most Intel and AMD Windows 11 PCs | None |
| `win-arm64-self-contained` | Windows 11 ARM64 devices | None |
| `win-x64-requires-dotnet8` | Intel/AMD PCs with .NET already installed | .NET 8 Desktop Runtime |
| `win-arm64-requires-dotnet8` | ARM64 devices with .NET already installed | .NET 8 Desktop Runtime |

The self-contained builds are larger because they include the .NET and WPF runtimes. The `requires-dotnet8` builds are much smaller but require the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

## System requirements

- Windows 11
- Administrator privileges are normally not required
- Framework-dependent builds require Microsoft .NET 8 Desktop Runtime

## Settings

The settings window follows the Windows light or dark theme. The interface supports English and Danish, with English as the default language. Changes are applied when you select **Save**. **Cancel** closes the window without saving.

### General

- **Language:** Choose English or Danish.
- **Start with Windows:** Start Minimemizer automatically when you sign in.
- **Open thumbnail:** Choose single-click or double-click.
- **Right-click menu:** Show the application's classic window menu, including Restore, Maximize, and Close.

### Appearance

- Choose the maximum thumbnail width and height.
- Use an adaptive size or give every thumbnail a uniform size.
- With a uniform size, crop the window to the frame or show the entire window.
- Choose no frame, square corners, or rounded Windows 11 corners.
- Adjust thumbnail opacity with the slider.
- Show or hide the application icon and position it along the top or bottom of the thumbnail.

A live preview shows how appearance changes will look.

### Placement

- Select the display where thumbnails should appear.
- Select a starting corner.
- Arrange thumbnails horizontally or vertically.
- Adjust the spacing between thumbnails and the display edge.

If the available space is insufficient, all thumbnails are scaled down automatically.

### Applications

Add applications that Minimemizer should ignore. Exclusions are stored using the application's `.exe` path.

### About

Displays the Minimemizer version and the architecture of both the application build and the Windows system. Version 0.5.4 is available for ARM64 and x64.

## Everyday use

- A thumbnail is created automatically when an application is minimized.
- The thumbnail is removed when the application is restored or closed.
- Right-click a thumbnail to open the application's classic Windows menu when the option is enabled.
- Exit Minimemizer through the system tray icon. If it becomes unresponsive, it can be closed using Task Manager.

## Known limitations

- Windows does not provide an official API for placing third-party elements directly between the desktop background and desktop icons. Thumbnails are therefore placed at the bottom of the regular window order.
- Protected video, DRM content, and some specially rendered applications may display a black, empty, or frozen preview.
- Applications running with elevated administrator privileges may not always be fully controllable by a normally started Minimemizer process.
- Windows 11 taskbar jump lists cannot be opened for other applications through a public Windows API. Thumbnail right-click therefore uses the classic window menu.

## Troubleshooting

### No thumbnail appears

- Confirm that Minimemizer is still running in the system tray.
- Check the **Applications** settings page to ensure the application is not excluded.
- Restore the application and minimize it again.
- Restart Minimemizer.

### The thumbnail is black or frozen

The application may use protected or specialized rendering that Windows does not expose as a live thumbnail. This cannot always be worked around.

### Settings look incorrect after an update

Exit all older Minimemizer processes through the system tray or Task Manager, then start the latest `Minimemizer.exe`.

Settings are stored for the current Windows user in:

```text
%APPDATA%\Minimemizer\settings.json
```

Deleting this file restores the default settings on the next launch.

## Development

Run from source:

```powershell
dotnet run
```

Create a self-contained Windows x64 build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Create a self-contained Windows ARM64 build:

```powershell
dotnet publish -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## License

No license file has been added yet. All rights are reserved unless a license is added to the repository.
