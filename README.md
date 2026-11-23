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
| Left | App stickers, including the shortcuts from your Windows desktop. Click to open. Drag one and it snaps to the nearest cell of a grid; drop it on another sticker and the two swap places. Right-click to open or unpin (an unpinned desktop shortcut stays gone). |
| Bottom centre | The dock: search, layouts, show desktop (minimizes every window; press it again to bring them back), then a few pinned apps and **every app that is running** (with a dot for each open one, longer for the window you are using), then calendar, volume, network and the control center. Click an app to open or switch to it, or to minimize it if it is the one you are using; right-click for its windows, a new window, and close. |
| Bottom right | Greeting and clock. |

The dock registers with Windows as an app bar, so windows tile above it. **Appearance → Dock edge** moves it to the left or right, and **Dock size** makes it smaller or larger.

### The Windows taskbar

While the Bluehour desktop is up, **the Windows taskbar is replaced by the dock**: Bluehour tells Windows to auto-hide it (which frees the screen edge for windows) and then hides the taskbar windows, on every display, so it can never pop up over the dock. Nothing is closed or modified, so it is fully reversible. The taskbar comes back when you choose **Return to Windows**, **Exit Bluehour** or **Recovery → Revert Bluehour changes**, if Bluehour crashes or is closed, and the next time Bluehour starts if it was ended abruptly. Turn the replacement off under **Appearance → Replace the Windows taskbar…**. If it ever stays hidden, open `BluehourDesktop.exe` again, or restart Explorer from Task Manager.

The notification area (tray icons) lives on the taskbar, so it is hidden with it; Bluehour does not replace it. The dock has calendar, volume and network buttons instead. If you need the tray icons, turn the replacement off or use **Show desktop**'s sibling, **Return to Windows**.

### The Windows key

While the Bluehour desktop is up, **tapping the Windows key opens the Bluehour launcher instead of the Start menu**, and tapping it again closes it. Shortcuts that use the key (Win + E, Win + Tab, Win + D, and so on) are untouched, because only a lone tap is intercepted. The hook is installed only while the desktop is up and is removed when you return to Windows or exit. Turn it off under **Appearance → Shortcuts**; **Ctrl + Alt + K** always opens the launcher either way.

Buttons react the way the site's do: they sit on a hard shadow, lift a little when hovered, and sink onto the shadow while pressed. A click counts when the mouse is released over the button that was pressed, or with Space or Enter from the keyboard.

## Windows in the workspace

Windows opened from a sticker, the dock, or the launcher are framed and tiled even if automatic tiling is off. With **Workspaces → Tile windows automatically** on (the default), every new window on the selected display joins the workspace, and the layout adjusts when windows open, close, minimize or are maximized.

- **Frames.** Under **Performance → Window frames** there are three styles. **Native** (the default on Windows 11) asks Windows itself to draw a coloured border and round corners: the border turns accent-coloured on the focused window, and it costs nothing because there is no extra window. A window that fills its display (a video in full screen, F11, a maximized window) gets no border and square corners. **Sticker** draws kuro's look (plum edge, hard shadow, glow) with an extra window per app and clips the app's corners, which is heavier. **Off** only tiles. Windows 10 has no Native style, so it starts on Off.
- **Motion.** By default (**Performance → Window movement → Quick**) windows slide to their tiles in about a fifth of a second: the window is resized once and then only moved, which Windows does without the app redrawing, so it stays smooth on 120 and 144 Hz monitors. A new app opens straight into its tile and Windows' own open animation plays. **Smooth** is Ivy's 0.34 s glide with the 94% grow-in, which resizes every frame and is heavier. Steps are paced by the display's refresh rather than a timer. If Windows' *Show animations* setting is off, everything snaps instead.
- **Rearranging.** Drag a window by its title bar and a soft preview tile shows where it will snap. Release over a tile (or over the gap nearest to one) and the two windows swap. Release over its own tile, or off the tiled area, and it goes back. In Cascade, windows stay where you drop them.
- **Choosing a layout** (top switcher, dock, or launcher) tiles every open window on the display, including ones that were maximized.
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

## Search

Tap the **Windows key**, press **Ctrl + Alt + K**, or click search. One box finds:

- **Apps**: everything installed (Start menu and Store apps), your Windows desktop shortcuts, and Bluehour's own. Typing letters in order works, so `vsc` finds Visual Studio Code and `disc` finds Discord. What you open most rises to the top.
- **Open windows**, to switch to.
- **Windows settings and tools**: Display, Sound, Startup apps, Game Mode, Task Manager, Device Manager and about thirty more.
- **Files and folders**, from the Windows Search index (the one File Explorer uses). If indexing is off, the other results still appear.
- **A calculator**: type `2*(3+4)` and Enter copies `14`.
- **Addresses and the web**: an address opens in your browser; anything else can be searched on Google.

With nothing typed it shows what you used recently, what is open, and **every app A to Z**, so nothing is hidden. `>` searches Bluehour's own commands. Arrow keys move, Enter opens, Esc closes, and clicking elsewhere dismisses it.

## Performance and gaming

Bluehour is built to cost as little as it can, and to get out of the way when you play. It cannot make a game faster than Windows would run it, but it should not make it slower, and the page below lets you check that and apply some ordinary Windows settings.

- **Idle cost.** With nothing happening Bluehour wakes a couple of times a second and does a few milliseconds of work. It has no polling loop and no animation timers while idle, and it only listens for the quiet window events (focus changes, move/resize, minimize). The control center builds its pages when you open it and frees them when you close it. Wallpapers are decoded at screen size, not kept at 4K.
- **Resting while you play.** When a game or any full-screen app is in front, Bluehour **rests**. Two things must agree before it does: Windows reports a full-screen app (the signal behind Focus Assist) *and* the window in front really covers its whole display, for two checks in a row. (Bluehour's own desktop covers the screen too, which Windows alone cannot tell from a game.) While resting: window tracking stops, its event hooks and the Windows key hook are removed, the dock is hidden (a topmost window above a game can cost it frames), and Bluehour drops to low priority and gives its memory back. It wakes by itself afterwards. **Ctrl + Alt + G** rests or wakes it by hand; the setting is under **Performance**.
- **See the cost.** **Performance → Bluehour right now** shows Bluehour's own CPU and memory, live.
- **Windows settings.** **Apply gaming settings** turns on Game Mode and turns off the Game Bar overlay and Game DVR capture. **High performance power plan**, **Turn off transparency** and **Turn off mouse acceleration** are separate buttons. Each is an ordinary Windows setting, journaled so **Recovery → Revert Bluehour changes** puts it back (the previous power plan is remembered).

## Game optimizer

Under **Performance → Game optimizer**. When a game comes to the front, Bluehour rests and then, for that game's profile:

- raises the **game's priority** (High, or Above normal on the Balanced level),
- runs **other apps one step lower** (Below normal) in your session, except Windows itself, audio, voice and capture apps, game stores, launchers and anti-cheat, and anything that lives in the game's own folder,
- **hands memory back** by trimming the working sets of the other apps, so the game has free RAM (the pages stay recoverable and apps fault them back in when used), and
- switches to the **High performance power plan**.

When the game ends every priority and the power plan are put back. Nothing is closed, suspended or killed. Each game has a profile (Max, Balanced or Off), plus two optional Windows settings: **High performance GPU** (the per-app graphics preference) and **No full-screen optimizations** (a compatibility flag; this suits some games and not others). Both are written through the journal, so **Recovery → Revert Bluehour changes** restores them. Games are learned as they run (a program must be a Direct3D full-screen game, or live in a game library, so a full-screen video is left alone), or use **Find my games** (Steam and Epic libraries) or **Add a game**. **Free memory now** does the memory step on demand. No optimizer can make hardware faster; this reduces what competes with the game.

## Live wallpapers

**Appearance → Choose image or video…** accepts an MP4 (also M4V, MOV and WMV). The video plays **muted and looping** behind the Bluehour desktop: it runs in its own window directly beneath the desktop surface, and the surface is painted with a colour key so the video shows through while the stickers, clock and buttons are drawn over it. **Stop live wallpaper** goes back to the picture.

- **Player.** It uses the Windows Media Player component, the decoder Windows itself uses for MP4 (H.264 video, AAC sound), so it plays on the graphics card and costs little processor time. If Windows Media Player is not installed (Windows 11: Settings → System → Optional features → *Windows Media Player Legacy*), Bluehour says so and keeps the picture. HEVC (H.265) needs Microsoft's HEVC extension.
- **If a video will not play**, the message says why: Bluehour reads the video and sound formats from the file (H.264, H.265/HEVC, AV1, VP9, AAC, Dolby and so on) and tells you when it is a format Windows lacks, with the fix (re-saving as H.264 in HandBrake is free). Big files get up to half a minute to open, and `diagnostics.log` records the player's state.
- **Pauses by itself** while a maximized or full-screen window covers the display, and while Bluehour rests for a game, so it costs nothing when you cannot see it.
- **Fit.** The video fills the display, cropped at the edges if its shape differs. It plays on the display Bluehour manages; other displays keep the picture.
- **The video is referenced, not copied.** If you move or delete the file, Bluehour tells you and falls back to the picture.
- **Limits.** The Windows desktop wallpaper itself is not changed (you see the normal one after Return to Windows). Where the surface shows the video, clicks go to the video window, so right-clicking an empty part of the desktop opens no menu in this mode (use the tray icon or the stickers); stickers, the clock and the top bar behave as usual. The edges of text and stickers drawn over the video fade to a dark outline. Ctrl + Alt + Backspace and the tray menu always return to Windows.

## Settings

Use **Workspaces** or the top switcher to choose a layout. Use **Control center** (top right, or **Dashboard**) for settings and recovery, and type `>` in the launcher to switch to commands. Under **Appearance**, choose the dock edge and size, the **accent** (Lavender, Sakura, Mint, or **any color you pick**). The accent tints the whole interface, not just the highlights: the grounds, cards, borders and text take on its hue, so a red accent gives a red-tinted Bluehour and a green one a green one. Lavender keeps the original kuro violet, wallpaper, Windows color mode, and the taskbar replacement. The **Desktop widgets** group turns each part of the desktop on or off: clock, greeting, date, an optional CPU and memory meter, the app stickers, the layout switcher, the search button and the control-center button; the dock's running apps and its calendar, volume and network buttons can be hidden too. **Performance → Window movement** picks Quick (default), Smooth (Ivy's full glide) or Off. Windows Task View remains available through **Win + Tab**.

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

The build uses the Windows .NET Framework C# compiler (C# 5) with warnings as errors. The test checks layout geometry, the window-glide curve, the Windows key tap detector, the calculator and search ranking, native window placement and restoration on disposable windows, rounding and releasing a window's corners, journal reload, and desktop and settings rendering. An exit code of `0` indicates success. To also write screenshots, pass a log path: `BluehourDesktop.exe --self-test C:\temp\log.txt`. The delivered executable is unsigned.

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
