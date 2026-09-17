# JBF.HudTest

Standalone test plugin for CS2 `custom_hud_layout` / Panorama clickable HUD.

## What it tests

- A left-center Panorama window.
- Mouse input capture.
- Click events reaching CounterStrikeSharp.
- Per-player text changes.
- Per-player CSS class changes.
- Closing the panel and releasing the cursor.

Command in game:

```text
!hudtest
```

or:

```text
css_hudtest
```

## Requirements

- CounterStrikeSharp 1.0.374 or newer.
- CS2 build with `custom_hud_layout` support.
- Counter-Strike 2 Workshop Tools on the PC used to compile Panorama resources.

The C# plugin alone is not enough. The client must have the compiled Panorama files.

## 1. Build the plugin

Build `JBF.HudTest.csproj` and place the normal CounterStrikeSharp plugin output on the server.

## 2. Compile the Panorama files on your Windows PC

Open PowerShell in the repository and run:

```powershell
powershell -ExecutionPolicy Bypass -File .\JBF.HudTest\hud\build.ps1
```

If CS2 is installed in a non-standard location:

```powershell
powershell -ExecutionPolicy Bypass -File .\JBF.HudTest\hud\build.ps1 -Cs2 "D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive"
```

The script:

1. copies `jbf_hud_test.xml` and `jbf_hud_test.css` into a CS2 addon content directory;
2. compiles them with `resourcecompiler.exe`;
3. copies the resulting `.vxml_c` and `.vcss_c` directly into your local CS2 client for quick testing.

Restart CS2 completely after compilation. Panorama resources are cached for the game session.

## 3. Start the server and test

Join the server from the same PC where the compiled Panorama files were copied and type:

```text
!hudtest
```

You should get a dark panel on the left side of the screen with three clickable rows and a Close button.

Expected behavior:

- `Проверить кнопку` changes the status text to confirm the click reached C#.
- `Сменить текст` changes the title without reopening the HUD.
- `Выделить пункт` applies the `selected` CSS class.
- `ЗАКРЫТЬ` hides the HUD and releases the cursor.

## Important

This local-copy method is only for testing on your own client. For real players the compiled Panorama resources must be distributed through a Workshop addon (for example with a server addon manager). Do not move JBF.Menu to this system until this standalone test works reliably on your server/client combination.
