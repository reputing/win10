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

The command palette, workspace switching, and dashboard controls were informed by the live [Ivy v2](https://ivy.rest/v2) desktop. Bluehour uses an original interface and artwork. The wallpaper is embedded from [coast.jpg](coast.jpg), created with built-in Imagegen using the prompt “Photorealistic widescreen 16:9 Windows wallpaper, panoramic northern fjord at blue hour, deep indigo water, misty violet-gray mountain layers, narrow warmly glowing horizon, broad reflective water, original high-detail landscape photography. No text, UI, people, logos or marks.”

Windows integrations use the [Shell AppBar API](https://learn.microsoft.com/en-us/windows/win32/shell/application-desktop-toolbars), [window placement API](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-windowplacement), and [System Restore](https://learn.microsoft.com/en-us/windows/win32/sr/using-system-restore).
