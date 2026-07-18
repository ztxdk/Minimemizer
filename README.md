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
3. Choose whether to install for the current user, install for all users, or continue in portable mode.
4. Minimize a regular application.
5. A thumbnail of the application appears on the desktop.
6. Single- or double-click the thumbnail to restore the application.

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

The settings window follows the Windows light or dark theme. The interface supports English and Danish, with English as the default language. **Apply** saves and previews changes without closing the settings window, while **Save** saves and closes it. **Cancel** closes the window without saving changes made since the last apply.

### General

- **Language:** Choose English or Danish.
- **Start with Windows:** Start Minimemizer automatically when you sign in.
- **Check for updates automatically:** Check GitHub Releases at most once per day. Downloads and installation always require confirmation.
- **Open thumbnail:** Choose single-click or double-click.
- **Right-click menu:** Show the application's classic window menu, including Restore, Maximize, and Close.

### Appearance

- Choose the maximum thumbnail width and height.
- Use an adaptive size or give every thumbnail a uniform size.
- With a uniform size, crop the window to the frame or show the entire window.
- Choose no frame, square corners, or rounded Windows 11 corners.
- Adjust thumbnail opacity with the slider.
- Show or hide the application icon and position it along the top or bottom of the thumbnail.
- Show the full window title on hover, hide it, or keep a shortened title visible inside or in a full-width bar above the thumbnail.

A live preview shows how appearance changes will look.

### Placement

- Select the default display and corner for thumbnails without a zone rule.
- Arrange thumbnails horizontally or vertically.
- Adjust the spacing between thumbnails and the display edge.
- Drag a thumbnail to any corner on any connected display to move that window into the highlighted zone.
- Move an individual thumbnail only by dragging it. After dragging it away from its default corner, use `⋯` to pin other open thumbnails from the same application there or make that corner the application's default.

Each display has four independent corner zones. If a single row or column does not fit, the zone wraps thumbnails into additional rows or columns before scaling them down.
Dragging changes only the selected window and is remembered while that window exists. If a configured display is disconnected, its thumbnails temporarily fall back to the default display and return when the display is available again.

### Applications

Add applications that Minimemizer should ignore, and manage default zone rules for specific applications. Exclusions and zone rules are stored using the application's `.exe` path. Settings uses a compact visual display/corner picker when a zone rule is added or changed.

### About

Displays the version, architecture, update status, and installation mode. Version 0.7.0 is available for ARM64 and x64. This page can check for updates, install a verified update, install a portable copy, or uninstall an installed copy.

## Installation, updates, and removal

- **Current user:** Installs to `%LOCALAPPDATA%\Programs\Minimemizer` without administrator rights.
- **All users:** Installs to `%ProgramFiles%\Minimemizer` and requests UAC only for installation, updates, and removal.
- **Portable:** Runs from its current path. This choice is remembered for that exact path.
- Pass `--portable` to bypass the installation question for a scripted or temporary run.
- Start menu shortcuts are enabled by default; desktop shortcuts are optional.
- Installed copies appear in Windows **Installed apps**. Removal keeps personal settings unless deletion is explicitly selected.

Stable updates come from the latest GitHub Release. Minimemizer selects the self-contained x64 or ARM64 asset for the Windows architecture, verifies its size and GitHub-provided SHA-256 digest, closes the running copy, keeps a rollback copy, installs the update, and restarts. Portable copies can also update when their folder is writable.

Automatic checks require the GitHub repository and its release assets to be anonymously readable. A private repository returns an update error unless releases are moved to a public distribution endpoint.

## Everyday use

- A thumbnail is created automatically when an application is minimized.
- Thumbnail windows are excluded from Windows Task View and Alt+Tab.
- The thumbnail is removed when the application is restored or closed.
- After dragging a thumbnail away from its default corner, select `⋯` to pin the application's other open thumbnails there or make the corner its persistent default.
- Right-click a thumbnail to open the application's classic Windows menu when the option is enabled.
- Starting another copy prompts before closing the running instance and replacing it with the new one.
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

### Regression checks

When changing thumbnail ownership, Task View/Alt+Tab behavior, native window styles, or activation handling, verify both values of **Open thumbnail**:

- In **Double-click** mode, one click must not restore the source window; a double-click must restore it.
- In **Single-click** mode, one click must restore the source window.
- Thumbnail and icon-badge windows must remain absent from Task View and Alt+Tab.

Do not restore the source window from thumbnail activation messages such as `WM_ACTIVATE`. A normal mouse click can activate a window before the configured single-/double-click handler runs, which makes both settings behave as single-click.

Create a self-contained Windows x64 build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Create a self-contained Windows ARM64 build:

```powershell
dotnet publish -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The updater expects release assets to use these exact suffixes:

```text
-win-x64-self-contained.exe
-win-arm64-self-contained.exe
```

GitHub must expose a `sha256:` digest for an asset before the built-in updater will install it.

## License

No license file has been added yet. All rights are reserved unless a license is added to the repository.
