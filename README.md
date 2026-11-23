# Bluehour Desktop

Bluehour is a desktop customization app for Windows 10 x64. It draws its own desktop surface on each active display, puts a movable dock on the selected display, and arranges actual Windows application windows. The controller runs as the current user.

## Run

1. Open `BluehourDesktop.exe` and choose **Switch to Bluehour desktop**.
2. Use **Workspaces** or the dock's layout menu to put apps in Grid, Focus, Columns, Rows, or Cascade. Automatic tiling is optional. In Focus mode, the first included app gets the large pane.
3. Use **Open windows** to switch apps, **Control center** for settings and recovery, or **Ctrl + Alt + K** to search. Type `>` in the launcher to switch to commands.
4. Use **Return to Windows** or **Ctrl + Alt + Backspace** to dismiss the desktop surface, restore the previous taskbar setting, and undo recorded window positions. Use **Recovery → Revert Bluehour changes** for the Windows setting backups too.

The desktop surface covers the standard desktop while active. Explorer continues to provide Windows, task switching, and the underlying desktop. Windows apps keep their own interface. Under **Appearance**, choose the dock edge, accent, wallpaper, Windows color mode, and whether Windows auto-hides its taskbar in desktop mode. If an app enforces minimum dimensions, a layout can leave overlap or require fewer windows. Windows Task View remains available through **Win + Tab**.

**Cleanup** can disable selected per-user sign-in entries and Game DVR capture. Choose optional Store apps individually to remove them. Bluehour asks Windows to create and verify a new restore point before removal. System Protection, administrator approval, and Windows' restore point interval can affect availability. Reinstallation uses Microsoft Store, subject to availability. App removal can delete local app data. **Discord** opens the installed client or the official web app. **Install Discord** uses WinGet when present and the official download page otherwise.

The rollback journal is stored under `%LOCALAPPDATA%\BluehourDesktop`. Window positions can be recovered across a Bluehour restart while the original windows are still open. If Bluehour exits unexpectedly with the taskbar auto-hidden, reopen the EXE and use **Return to Windows**, or use Windows' own **Taskbar settings**.

## Build and verify

On Windows x64, run in Windows PowerShell from this directory:

```powershell
.\build.ps1
$p = Start-Process .\BluehourDesktop.exe -ArgumentList '--self-test' -Wait -PassThru -WindowStyle Hidden
$p.ExitCode
```

The test checks layout geometry, native window placement and restoration on disposable windows, journal reload, and desktop and settings rendering. An exit code of `0` indicates success. The compiled app uses the Windows .NET Framework and embeds its wallpaper. The delivered executable is unsigned.

## Reference and artwork

The rail, command palette, workspace switching, clock, and dashboard were informed by the live [Ivy v2](https://ivy.rest/v2) desktop. Bluehour uses a native Windows implementation with its own icon drawings and an original daylight Lofoten-inspired wallpaper. The embedded image is [coast.jpg](coast.jpg).

The interface embeds regular, bold, and display instances derived from [Google Sans Flex](https://github.com/google/fonts/tree/main/ofl/googlesansflex). The static instances use local Bluehour family names. Their license is included in [FONT-LICENSE.txt](FONT-LICENSE.txt) and embedded in the EXE. Fonts are registered for the lifetime of the Bluehour process.

## Updating and personalizing Windows

Exit the earlier build from its system tray menu before opening BluehourDesktop-3.exe. The new executable uses the same rollback journal. If you enabled sign-in launch, choose **Start at sign-in** again from the new executable to update its path.

**Appearance → Apply palette to Windows** sets Windows dark mode and the active and inactive title-bar colors. Apps with their own drawing code control which Windows settings they honor. **Recovery → Revert Bluehour changes** restores the saved registry values.

The top handle opens Workspaces. The side handle opens Control center. Right-click the desktop for the window switcher, File Explorer, appearance settings, and **Return to Windows**.

## App bubbles and browsing

Use **Dashboard → Your apps → Pin an app** to search Windows' installed applications and Start menu shortcuts. **Browse** also accepts a local executable or shortcut. Browser, Files, and Discord are available on first launch. Bluehour adds up to two recognized installed apps during first discovery. Right-click a bubble to unpin it.

Click a bubble to open the app. Drag it on the dashboard to change its order. Drag it on the desktop to place it anywhere inside the wallpaper area. The journal stores pinned apps, dashboard order, and relative desktop positions. Desktop positions apply across connected displays. Bubbles animate on hover and settle into their dashboard slots after a drop when Windows menu animations are enabled.

The Browser bubble opens Google in the registered default browser. Type an HTTP or HTTPS address into the launcher to open that address. The launcher can also send a search to Google when you choose **Search the web**. The settings search filters the navigation by page name and description.
