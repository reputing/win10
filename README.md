# Bluehour Desktop

Bluehour is a desktop customization app for Windows 10 x64. It draws its own desktop surface on each active display, floats a dock along the bottom edge, and arranges actual Windows application windows into a tiled workspace with rounded frames and animation. The controller runs as the current user.

The look is taken from [kuro.rip](https://kuro.rip): a near-black violet ground, lavender and pink accents, plum edges, chunky "sticker" shapes with a hard offset shadow, lowercase labels, and springy motion. Window motion (the 0.34 s glide and the pop-in) follows [Ivy v2](https://ivy.rest/v2).

## Run

1. Open `BluehourDesktop.exe` and choose **Switch to Bluehour desktop**.
2. Open apps from the **sticker column** on the left, the **dock** along the bottom, by tapping the **Windows key**, or with **Ctrl + Alt + K**. They open into the workspace and tile automatically.
3. Change how windows are arranged with the **layout switcher** at the top (Grid, Focus, Columns, Rows, Cascade). In Focus, the newest window gets the large pane.
4. Use **Return to Windows** (right-click the desktop, the tray menu, or **Ctrl + Alt + Backspace**) to dismiss the desktop surface, restore the previous taskbar setting, and put every window back where it was. **Recovery → Revert Bluehour changes** also restores the Windows setting backups.

## Starting up

Bluehour shows a loading screen straight away and sets the desktop up one step at a time behind it (settings, control center, taskbar, dock, wallpaper, windows, Windows key), so the app keeps responding and Windows never sees it as frozen. The same loading screen appears when you press **Switch to Bluehour desktop**. A big wallpaper or a busy PC makes it take longer; the screen says so after a few seconds.

If Bluehour ever does stop responding, it writes down where. Send `%LOCALAPPDATA%\BluehourDesktop\diagnostics.log`: it records the last step that was running whenever the interface stalls for more than two seconds, and any startup step or window update that takes unusually long.

## The desktop

| Where | What |
| --- | --- |
| Top centre | Layout switcher. Click a layout; the highlight springs across and the windows glide into place. |
| Top right | Search (**Ctrl + Alt + K**) and the control center. |
| Left | App stickers. Click to open, drag to move or reorder, right-click to unpin. |
| Bottom centre | The dock: search, layouts, show desktop, Files, Discord, calendar, volume, network, control center. Hover for a label. |
| Bottom right | Greeting and clock. |

The dock registers with Windows as an app bar, so windows tile above it. **Appearance → Dock edge** moves it to the left or right.

### The Windows key

While the Bluehour desktop is up, **tapping the Windows key opens the Bluehour launcher instead of the Start menu**, and tapping it again closes it. Shortcuts that use the key (Win + E, Win + Tab, Win + D, and so on) are untouched, because only a lone tap is intercepted. The hook is installed only while the desktop is up and is removed when you return to Windows or exit. Turn it off under **Appearance → Shortcuts**; **Ctrl + Alt + K** always opens the launcher either way.

Buttons react the way the site's do: they sit on a hard shadow, lift a little when hovered, and sink onto the shadow while pressed. A click counts when the mouse is released over the button that was pressed, or with Space or Enter from the keyboard.

## Windows in the workspace

Windows opened from a sticker, the dock, or the launcher are framed and tiled even if automatic tiling is off. With **Workspaces → Tile windows automatically** on (the default), every new window on the selected display joins the workspace, and the layout adjusts when windows open, close, minimize or are maximized.

- **Frames.** Each window gets a plum edge, rounded corners and a hard shadow. The edge turns accent-coloured and glows when the window is focused.
- **Motion.** Windows glide to their tiles over 0.34 s and new windows grow in from 94%. If Windows' *Show animations* setting is off, everything snaps instead.
- **Rearranging.** Drag a window by its title bar onto another tile to swap them. Dropping it anywhere else puts it back. In Cascade, windows stay where you drop them.
- **Maximize.** A window you maximize stays maximized until you restore it. Minimized windows leave the layout so the others fill the space.
- **Apps keep their own title bars.** Chrome, Discord and most Store apps draw their own, so Bluehour frames the window rather than replacing its title bar. Fixed-size windows and dialogs are left alone.
- **Limits.** If an app enforces a minimum size, its tile can overlap a neighbour. Apps running as administrator cannot be moved by Bluehour. Some apps reset their own window shape when they resize; Bluehour re-applies the rounded corners.

The frames and corner rounding are removed when you return to Windows, undo placement, or exit. If Bluehour is killed instead, rounded corners stay on already-open windows until those apps are reopened.

## Opening apps and Discord

Clicking a sticker, dock button or launcher entry brings that app's window forward if it is open, and otherwise starts it and frames its first window. A short message appears at the bottom of the desktop (for example "opening discord"), and a problem is reported there instead of only in the hidden control center.

**Discord** is opened in this order:

1. The installed client through its own launcher (Stable, PTB or Canary under `%LOCALAPPDATA%`).
2. Any install that registers the `discord://` protocol.
3. A Discord shortcut in the Start menu.
4. Discord's web app in its own Edge or Chrome app window, so it is framed like any other app.

If none works, **Cleanup → Install Discord** uses WinGet when present and the official download page otherwise.

## Settings

Use **Workspaces** or the top switcher to choose a layout. Use **Control center** (top right, or **Dashboard**) for settings and recovery, and type `>` in the launcher to switch to commands. Under **Appearance**, choose the dock edge, accent (Lavender, Sakura or Mint), wallpaper, Windows color mode, and whether Windows auto-hides its taskbar in desktop mode. Windows Task View remains available through **Win + Tab**.

**Cleanup** can disable selected per-user sign-in entries and Game DVR capture. Choose optional Store apps individually to remove them. Bluehour asks Windows to create and verify a new restore point before removal. System Protection, administrator approval, and Windows' restore point interval can affect availability. Reinstallation uses Microsoft Store, subject to availability. App removal can delete local app data.

The rollback journal is stored under `%LOCALAPPDATA%\BluehourDesktop`. Window positions can be recovered across a Bluehour restart while the original windows are still open. If Bluehour exits unexpectedly with the taskbar auto-hidden, reopen the EXE and use **Return to Windows**, or use Windows' own **Taskbar settings**.

Settings from earlier builds upgrade automatically: tiling turns on, the dock moves to the bottom, and the old accent names map to Lavender, Mint and Sakura.

## Build and verify

On Windows x64, run in Windows PowerShell from this directory:

```powershell
.\build.ps1
$p = Start-Process .\BluehourDesktop.exe -ArgumentList '--self-test' -Wait -PassThru -WindowStyle Hidden
$p.ExitCode
```

The build uses the Windows .NET Framework C# compiler (C# 5) with warnings as errors. The test checks layout geometry, the window-glide curve, the Windows key tap detector, native window placement and restoration on disposable windows, rounding and releasing a window's corners, journal reload, and desktop and settings rendering. An exit code of `0` indicates success. To also write screenshots, pass a log path: `BluehourDesktop.exe --self-test C:\temp\log.txt`. The delivered executable is unsigned.

## Fonts and artwork

The interface embeds static instances of [Figtree](https://github.com/google/fonts/tree/main/ofl/figtree) (text) and [Fredoka](https://github.com/google/fonts/tree/main/ofl/fredoka) (numerals and headings), both under the SIL Open Font License. The instances only pin the variable axes and use local Bluehour family names. The licenses are in [FONT-LICENSE.txt](FONT-LICENSE.txt) and embedded in the EXE. Fonts are registered for the lifetime of the Bluehour process.

All icons, including the cat, the sparkles and the layout glyphs, are drawn in code. The embedded wallpaper is an original daylight Lofoten-inspired image, [coast.jpg](coast.jpg). No artwork from kuro.rip or Ivy is included.

## Updating and personalizing Windows

Exit the earlier build from its system tray menu before opening a new executable. The new executable uses the same rollback journal. If you enabled sign-in launch, choose **Start at sign-in** again from the new executable to update its path.

**Appearance → Apply palette to Windows** sets Windows dark mode and the active and inactive title-bar colors. Apps with their own drawing code control which Windows settings they honor. **Recovery → Revert Bluehour changes** restores the saved registry values.

## App stickers and browsing

Use **Dashboard → Your apps → Pin an app** to search Windows' installed applications and Start menu shortcuts. **Browse** also accepts a local executable or shortcut. Browser, Files, and Discord are available on first launch. Bluehour adds up to two recognized installed apps during first discovery. Right-click a sticker to unpin it.

Drag a sticker on the dashboard to change its order. Drag it on the desktop to place it anywhere on the screen; stickers you have not moved stack down the left edge. The journal stores pinned apps, dashboard order, and relative desktop positions.

The Browser sticker opens Google in the registered default browser. Type an HTTP or HTTPS address into the launcher to open that address. The launcher can also send a search to Google when you choose **Search the web**. The settings search filters the navigation by page name and description.
