# NovaCore

NovaCore is an ongoing project focused on developing a lightweight, customizable desktop environment for Windows 11.

The project is built around the idea of providing a modular alternative to the standard Windows desktop experience, with individual components developed independently and gradually integrated into a unified environment.

NovaCore is currently in an early stage of development. **TinyShell** is the first major shell-focused component of the project and provides the foundation for replacing and extending parts of the default `explorer.exe` experience.

> **Status: Experimental / Alpha**
>
> TinyShell can replace the Windows Explorer shell for a user session. Read the recovery instructions before enabling it as a logon shell.

---

## Project Structure

NovaCore is not limited to a single application or shell. It is intended to serve as the foundation for a collection of components that together form a complete desktop environment.

### TinyShell

TinyShell is the current shell-focused component of NovaCore.

It provides the basic functionality required for a custom Windows shell, including:

- Custom desktop
- Taskbar / AppBar
- Start menu and application search
- File manager
- Desktop icons and persistent layouts
- Native Windows shell context menus
- Recycle Bin integration
- Fullscreen application detection and taskbar auto-hide
- Power management controls
- Volume and network controls
- Multi-monitor support
- Persistent configuration
- Optional Explorer shell replacement
- Debloat / Windows-tuning console

TinyShell is currently the primary development focus and serves as a foundation for future NovaCore components.

---

## Goals

NovaCore aims to provide a desktop environment that is:

- Lightweight
- Modular
- Customizable
- Responsive
- Native to Windows
- Easy to extend

Rather than modifying the existing Windows desktop indefinitely, the project explores what a Windows desktop could look like when its core components are implemented independently.

The goal is not to reproduce every feature of Windows Explorer, but to build a clean foundation that can be expanded as the project develops.

---

## What's New in the Alpha Build

The previous README mainly described the project architecture and feature goals. The supplied TinyShell Alpha source adds a much more complete user-facing shell workflow and the documentation below reflects that implementation.

### Shell replacement and recovery

- Added `--shell` mode for running TinyShell as the active Windows shell.
- Added installation of TinyShell as the per-user logon shell through:
  `HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell`
- Added an in-app **Install TinyShell as logon shell** / **Uninstall TinyShell as logon shell** action.
- Added `Ctrl+Alt+Shift+E` emergency recovery hotkey.
- Added `--restore-explorer` for restoring `explorer.exe` and removing the TinyShell shell registration.
- Added direct **Restore explorer.exe** and **Open Task Manager** actions to the TinyShell menu.

### Taskbar and multi-monitor support

- One taskbar is created per detected monitor.
- The taskbar can be moved to the:
  - Top
  - Bottom
  - Left
  - Right
- Built-in taskbar size presets:
  - Small
  - Medium
  - Large
- The taskbar can also be resized manually with the edge resize grip.
- Window buttons are tracked per monitor.
- Fullscreen applications can automatically hide the taskbar.

### Desktop

- Wallpaper is loaded from the Windows desktop configuration.
- Desktop files and folders are displayed as icons.
- Recycle Bin is exposed as a special desktop item.
- Desktop icons can be selected, opened and repositioned.
- Desktop layout is persisted between sessions.
- The desktop monitors common Desktop folders for file changes.

### Start menu / launcher

- Scans the Windows Start Menu shortcuts.
- Supports application search.
- Launches applications directly.
- Supports **Run as administrator** from an application's context menu.
- Supports opening the target location and copying the target path.

### File manager

- Built-in lightweight file browser.
- Directory navigation and path entry.
- File/folder icons.
- File sizes and modification dates.
- Create new folder.
- Rename.
- Delete through the Recycle Bin.
- Refresh and parent-directory navigation.
- Uses Windows' native shell context menu for operations such as Copy, Paste, Delete, Properties and registered shell extensions.

### System controls

The primary taskbar provides access to:

- Volume control and mute
- Network / Wi-Fi information
- Windows Settings
- Sleep
- Restart
- Shut down
- Sign out
- Task Manager

### Debloat console

TinyShell includes a Windows tuning / debloat console with:

- Searchable tweak catalog
- Safe / Balanced / Aggressive presets
- Per-tweak selection
- Risk indicators
- Optional System Restore checkpoint
- Administrator elevation when required
- Execution log

> The Debloat console is a separate advanced tool. Some tweaks can modify AppX packages, registry policies, services and scheduled tasks. Aggressive tweaks may break Windows features.

---

## Requirements

For the supplied Alpha build:

- Windows 11
- .NET 8 SDK for building from source
- x64 Windows build
- WPF / Windows Forms desktop tooling
- Visual Studio can be used with the solution and a Windows desktop .NET workload

The project currently targets:

`net8.0-windows10.0.19041.0`

The provided build script targets:

`win-x64`

---

# Building TinyShell

## Option 1 - Build with `BUILD-WINDOWS.cmd`

Open a terminal in the `TinyShell` directory and run:

```bat
BUILD-WINDOWS.cmd
```

The script builds the solution in **Release** mode:

```bat
dotnet build TinyShell.sln -c Release -r win-x64
```

Use the Release build for real shell testing. Debug builds are useful during development, but they are not the recommended build for replacing Explorer.

The resulting executable is normally found under a path similar to:

```text
TinyShell\bin\Release\net8.0-windows10.0.19041.0\win-x64\
```

Look for:

```text
TinyShell.exe
```

## Option 2 - Visual Studio

1. Open `TinyShell.sln`.
2. Select the **x64** platform.
3. Select the **Release** configuration.
4. Build the solution.
5. Use the generated `TinyShell.exe` for testing.

---

# First Run: Test `--shell` Before Installing Anything

**Do this before enabling TinyShell as the Windows logon shell.**

Run:

```bat
TinyShell.exe --shell
```

or launch the same executable from a shortcut with:

```text
TinyShell.exe --shell
```

When `--shell` starts, TinyShell intentionally terminates `explorer.exe` and starts the custom shell experience.

Test all of the important basics:

- Desktop and wallpaper
- Desktop icons
- Right-click menu
- Taskbar
- Start menu
- Application launching
- File manager
- Taskbar positioning and sizing
- Power controls
- Fullscreen hiding

### Emergency recovery

At any time during `--shell` testing, press:

```text
Ctrl + Alt + Shift + E
```

TinyShell will remove its shell registration and launch `explorer.exe`.

You can also run:

```bat
TinyShell.exe --restore-explorer
```

This performs the same recovery path from the command line.

> **Always verify this recovery path before installing TinyShell as the logon shell.**

---

# Installing TinyShell as the Windows Logon Shell

Once `--shell` mode works correctly, TinyShell can be installed as the shell used at user logon.

## Step 1 - Build in Release mode

Build TinyShell using `BUILD-WINDOWS.cmd` or Visual Studio with:

```text
Release / x64
```

Do not start the installation process with an untested Debug build.

## Step 2 - Test with `--shell`

Run:

```bat
TinyShell.exe --shell
```

Confirm the desktop, wallpaper, right-click menu and taskbar behave correctly.

If something goes wrong, press:

```text
Ctrl + Alt + Shift + E
```

## Step 3 - Open the TinyShell system menu

Open the TinyShell menu from the primary taskbar.

The menu is available from the primary taskbar's menu button and also from the shell's desktop/taskbar context menu.

## Step 4 - Select "Install TinyShell as logon shell"

Choose:

```text
Install TinyShell as logon shell
```

TinyShell writes its executable path with the `--shell` argument to:

```text
HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell
```

The installation changes the **current user's** shell registration. It does not intentionally replace the running session immediately; the current session can continue until the next logon.

## Step 5 - Sign out and sign back in

A fresh logon is required.

Windows reads the `Winlogon\Shell` value during logon, so simply closing and reopening TinyShell is not the same as testing the installed shell.

## Step 6 - Know the escape hatch before committing

If TinyShell does not start correctly after logon:

### Emergency hotkey

Press:

```text
Ctrl + Alt + Shift + E
```

TinyShell uses the registered hotkey to:

1. Remove the TinyShell shell registration.
2. Start `explorer.exe`.

### Command-line recovery

Use Run (`Win + R`) or another available terminal and run:

```bat
TinyShell.exe --restore-explorer
```

If `TinyShell.exe` is not in the current directory, use the full path to the executable.

This command:

1. Removes the TinyShell shell registration.
2. Starts `explorer.exe`.
3. Exits TinyShell.

---

# Uninstalling TinyShell as the Logon Shell

From the TinyShell system menu select:

```text
Uninstall TinyShell as logon shell
```

This removes the TinyShell value from the current user's Winlogon shell configuration when it is registered by TinyShell.

To restore the stock Explorer desktop in the current session, use:

```text
Restore explorer.exe
```

or:

```text
TinyShell.exe --restore-explorer
```

A sign-out/sign-in cycle is recommended after uninstalling the shell so that the next session starts normally.

---

# Command-Line Options

TinyShell currently supports these command-line modes:

### `--shell`

Starts TinyShell as the active shell and terminates `explorer.exe`.

```bat
TinyShell.exe --shell
```

### `--restore-explorer`

Removes the TinyShell shell registration, starts `explorer.exe`, and exits TinyShell.

```bat
TinyShell.exe --restore-explorer
```

### `--debloat`

Opens the TinyShell Debloat console directly.

```bat
TinyShell.exe --debloat
```

---

# Useful Hotkeys

| Hotkey | Action |
|---|---|
| `Ctrl + Alt + Shift + E` | Emergency restore of `explorer.exe` |
| `Ctrl + Shift + Esc` | Open Task Manager |

The emergency hotkey is registered by the primary TinyShell taskbar.

---

# Taskbar Configuration

The TinyShell taskbar can be placed on any monitor edge:

```text
Top
Bottom
Left
Right
```

Built-in sizes are:

```text
Small    = 36 px
Medium   = 46 px
Large    = 64 px
```

The taskbar also supports manual resizing within its supported range.

Configuration is persisted in:

```text
%LOCALAPPDATA%\TinyShell\settings.json
```

Current persisted settings include:

- Taskbar edge
- Taskbar thickness
- Desktop enabled/disabled

---

# Desktop Layout Storage

Desktop icon positions and desktop layout preferences are stored in:

```text
%LOCALAPPDATA%\TinyShell\desktop-layout.json
```

The layout system supports:

- Saved icon positions
- View size
- Sort mode
- Auto-arrange
- Grid alignment

---

# Recommended Test Checklist

Before installing TinyShell as a logon shell, verify:

- [ ] Release x64 build starts
- [ ] `TinyShell.exe --shell` starts correctly
- [ ] Explorer is replaced only during the test session
- [ ] Desktop and wallpaper display correctly
- [ ] Desktop icons open correctly
- [ ] Right-click shell menus work
- [ ] Start menu launches applications
- [ ] File manager opens and can navigate
- [ ] Recycle Bin operations work
- [ ] Taskbar tracks running windows
- [ ] Multi-monitor taskbars appear correctly
- [ ] Fullscreen applications hide the taskbar as expected
- [ ] Power controls work
- [ ] `Ctrl+Alt+Shift+E` restores Explorer
- [ ] `TinyShell.exe --restore-explorer` restores Explorer
- [ ] Only after all of the above: install TinyShell as the logon shell

---

# Known Limitations

NovaCore and TinyShell are experimental software.

### Fullscreen detection

Exclusive fullscreen is detected through the Windows AppBar mechanism. Borderless fullscreen is detected heuristically.

Because borderless fullscreen detection is heuristic, some applications may be missed or incorrectly classified.

### Vertical taskbars

The current implementation rotates the taskbar content for left/right placement rather than using a separate vertical layout.

As a result, text and window titles may appear rotated on vertical taskbars.

### Explorer replacement

`--shell` intentionally terminates `explorer.exe`. When TinyShell is used as the logon shell, Explorer is not started automatically for that session.

Do not enable shell replacement on an important production machine until the recovery path has been tested.

### Debloat

The Debloat console can make system-level changes. Some actions require administrator rights and may affect Windows packages, policies, services or scheduled tasks.

Use a restore point and review selected tweaks before applying them.

---

# Development

NovaCore is an ongoing project and should be considered experimental software.

The architecture, APIs, components, and user interface may change significantly during development. Some functionality is incomplete, and current components should not be considered production-ready.

Future development may include:

- Additional desktop components
- Improved shell integration
- Application management
- Notification area support
- Improved window management
- Further customization options

---

# Technology

The current implementation is primarily written in:

- C#
- .NET 8
- WPF
- Windows Forms components where required
- Native Win32 APIs
- Windows Shell APIs

The project makes use of native Windows functionality where appropriate rather than attempting to reimplement operating-system functionality unnecessarily.

---

# Current Status

NovaCore is not finished.

The current implementation represents an early stage of a larger project rather than a completed desktop environment. TinyShell currently provides the most developed part of the system, while other parts of the NovaCore architecture are still planned or under development.

Expect:

- Incomplete functionality
- Bugs
- Breaking changes
- UI changes
- Architectural changes
- Compatibility issues while the shell is evolving

---

# Safety and Recovery Notes

TinyShell changes the Windows desktop experience at a system-shell level when `--shell` mode is used.

Before enabling the logon shell:

1. Test `TinyShell.exe --shell`.
2. Test `Ctrl + Alt + Shift + E`.
3. Test `TinyShell.exe --restore-explorer`.
4. Keep a copy of the known-good Release executable.
5. Prefer testing on a controlled or secondary Windows installation.

Never assume that a development build is safe to use as your permanent Windows shell.

---

# Disclaimer

NovaCore is provided "as is" and is intended primarily for experimentation, development, and testing.

The project is unfinished and may contain bugs or behave unexpectedly. Features may be incomplete, unstable, or removed or changed without notice.

Using NovaCore, particularly as a replacement for the Windows Explorer shell, is done entirely at your own risk.

The author provides no guarantee regarding stability, reliability, compatibility, data integrity, or suitability for any particular purpose and assumes no responsibility for data loss, system instability, software conflicts, configuration changes, or any other damage resulting from the use of the project.

Users should maintain appropriate backups and should test NovaCore in a controlled environment before using it as their primary desktop shell.

NovaCore is not affiliated with, endorsed by, or supported by Microsoft.
