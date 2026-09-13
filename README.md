# TinyShell — feature batch: desktop, file manager, resize/reposition, power menu, auto-hide

## What's new, mapped to your list

1. **Auto-hide on fullscreen.** Two independent signals, combined:
   - `ABN_FULLSCREENAPP` via `SHAppBarMessage` — Windows' own notification
     for true exclusive-fullscreen DirectX apps. Reliable.
   - `FullscreenDetector` — a heuristic for **borderless** fullscreen
     (most modern games, video players, emulators): a window counts as
     fullscreen if its rect exactly covers a monitor and it has no
     caption/thickframe chrome. **This is a heuristic, not a guarantee**
     — it's the same approach every third-party taskbar-hider uses,
     because Windows has no official API for "is this borderless
     fullscreen." An unusual window (some caption-less utility, a
     screenshot tool) could theoretically false-positive.
2. **Fully functional desktop.** `Shell/DesktopWindow` now behaves like an
   actual desktop, not a static icon board:
   - **Select vs. open**: single click selects (cyan highlight), double
     click opens — matches standard desktop conventions. (The Start menu
     is intentionally different — single click there launches, since a
     menu isn't a persistent surface you select things on.)
   - **Drag to reposition**: icons can be dragged anywhere; positions
     persist to `%LOCALAPPDATA%\TinyShell\desktop-layout.json` and survive
     refreshes/restarts. Icons without a saved position auto-arrange in a
     column-major grid, same as Explorer.
   - **Keyboard**: F2 renames the selected icon, Delete sends it to the
     Recycle Bin (with confirmation), Enter opens it.
   - **Recycle Bin**: a real Recycle Bin icon (icon pulled from
     `shell32.dll` via `ExtractIconEx`, since `SHGetFileInfo` can't resolve
     an icon for a virtual shell item from a plain path string). Opens the
     real Recycle Bin; right-click offers Empty Recycle Bin.
   - **Delete is no longer permanent** — both the desktop and the file
     manager now route through `RecycleBin.Send` (`SHFileOperation` with
     `FOF_ALLOWUNDO`), not `File.Delete`/`Directory.Delete`.
   - **Right-click → New → Folder / Text Document**, same as Explorer,
     with an immediate rename prompt after creation.
   - **Auto-refresh**: a `FileSystemWatcher` on both Desktop folders means
     dropping a file onto the desktop from elsewhere shows up without
     manually refreshing.
   - **Focus model**: earlier this used `WS_EX_NOACTIVATE` to guarantee it
     never covered other windows, but that also blocks keyboard focus —
     which F2/Delete/Enter need. It's now a normal focusable window that
     re-asserts `HWND_BOTTOM` every time it's activated (via the
     `Activated` event), so clicking the desktop gives it keyboard focus
     like a real desktop, while it still snaps back behind every other
     window immediately after.
   - Only created in `--shell` mode, spans the full virtual desktop
     (all monitors).
3. **Minimize fix.** There's no official API to tell Windows "target my
   taskbar button" for the minimize animation without implementing the
   full `ITaskbarList` COM interface. The pragmatic fix (`ShellManager.
   DisableMinimizeAnimation`) turns the animation off entirely in shell
   mode — minimized windows disappear instantly instead of flying to a
   corner — and restores your normal setting on exit. Minimized apps
   were already appearing in the taskbar's window list (that part was
   already working); this just fixes the visual glitch.
4. **Single-click Start menu launch.** Replaced the `ListBox` with a
   plain `ItemsControl` of buttons — `Button.Click` already fires on a
   single left-click, no double-click needed.
5. **Right-click in the Start menu.** Each app tile: Open, Run as
   administrator, Open file location, Copy path.
6. **Right-click on the taskbar.** The whole bar background now opens
   the same menu as the gear icon (`RootLayout_MouseRightButtonUp`).
7. **Resize & reposition.**
   - *Reposition*: right-click → "Move taskbar to" → Top/Bottom/Left/Right.
     Applies to every monitor's bar at once and persists to
     `%LOCALAPPDATA%\TinyShell\settings.json`.
   - *Resize*: drag the thin grip on the bar's inward-facing edge, or
     right-click → "Taskbar size" for Small/Medium/Large presets.
   - **How vertical bars work**: rather than a second XAML layout, a
     left/right bar rotates its content 90°/-90° via `LayoutTransform`
     (which — unlike `RenderTransform` — participates in hit-testing, so
     clicks land correctly). Trade-off: text labels rotate too, so app
     and window names read sideways on a vertical bar. A proper vertical
     layout would need its own XAML template; this was the pragmatic
     choice for one pass.
8. **Power menu.** Shutdown/Restart/Sleep/Sign out, available from: the
   power icon on the primary bar, the bottom row of the Start menu popup,
   and the right-click menu's "Power" submenu. All confirm before acting.
9. **File manager.** `FileManager/FileManagerWindow` — address bar, up/
   refresh, sortable-by-type list with real shell icons, double-click to
   open/navigate, right-click for Open/Rename/Delete/Copy path, New
   Folder button. Delete is **permanent** (no Recycle Bin integration) —
   it warns before deleting. Opens from the taskbar's FILES button, the
   right-click menu, or double-clicking a folder on the desktop.
10. **Debloater: casual descriptions + more tweaks.** Every description
    rewritten in plain language (what it does, and what you'd notice if
    anything). New entries: classic Windows 10 right-click menu, force
    dark mode, "End task" on taskbar right-click, always show file
    extensions, show hidden files, Sticky Keys popup, Meet Now icon,
    lock screen ads, Fast Startup (flagged for dual-boot users), clear
    temp files, clear Windows Update cache.

## New files

```
Settings.cs              - BarSettings (edge/thickness), JSON-persisted
PowerActions.cs           - shutdown.exe/rundll32 wrappers
SimpleInputBox.cs         - tiny WPF prompt (used by Rename), avoids a
                            Microsoft.VisualBasic reference
Converters.cs             - dims minimized taskbar buttons
Shell/FullscreenDetector.cs
Shell/ShellIcon.cs         - SHGetFileInfo/ExtractIconEx-based icon extraction (files, folders, and virtual items like Recycle Bin)
Shell/RecycleBin.cs        - SHFileOperation wrapper, used by both the desktop and file manager
Shell/DesktopLayout.cs     - persists dragged icon positions
Shell/DesktopWindow.xaml(.cs)
FileManager/FileManagerWindow.xaml(.cs)
```

## Testing notes

- Resize/reposition and the power menu work in windowed mode too (no
  `--shell` needed) — safe to try without committing to a full session.
- The desktop window only appears in `--shell` mode. If you want to
  preview it without killing Explorer, temporarily change the condition
  in `App.xaml.cs`'s `OnStartup` (`if (shellMode && ...)` → drop the
  `shellMode &&`) for a test run, then revert.
- `DisableMinimizeAnimation` changes a **system-wide** Windows setting
  for the session. It's restored on normal exit (`OnExit`), but if
  TinyShell crashes hard instead of exiting cleanly, your minimize
  animation could stay off until you toggle it back (Settings → Ease of
  Access → Visual effects → Animation effects) or re-run TinyShell and
  exit it normally once.
- Escape hatches unchanged: Ctrl+Alt+Shift+E restores explorer.exe,
  Ctrl+Shift+Esc opens Task Manager.

## Still not implemented

- System tray / notification area hosting.
- UWP/packaged app launch via `AppUserModelID`.
- Marquee (rubber-band) drag-select of multiple desktop icons — only
  single-selection is implemented.
- A dedicated vertical-bar XAML layout (current one rotates the
  horizontal layout, so text reads sideways).
