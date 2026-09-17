# JBForsaken UI Workshop Addon

The shared Panorama UI used by `JBF.Menu` is shipped to players as a client-side Workshop addon.

## 1. Build/update the addon locally

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\JBF.Menu\hud\build.ps1 `
  -Cs2 "E:\Steam\steamapps\common\Counter-Strike Global Offensive"
```

The default addon name is:

```text
jbforsaken_ui
```

Sources are copied to:

```text
<CS2>\content\csgo_addons\jbforsaken_ui\panorama\...
```

Compiled resources are created under:

```text
<CS2>\game\csgo_addons\jbforsaken_ui\panorama\...
```

The script also updates the local loose Panorama files for development. Use `-NoLocalInstall` when preparing only the Workshop addon.

## 2. Publish with Counter-Strike 2 Workshop Tools

Open Counter-Strike 2 with Workshop Tools, select the `jbforsaken_ui` addon and publish it to the Workshop. On later UI updates, rebuild using the same addon name and update the existing Workshop item instead of creating a new one.

After publishing, save the Workshop item ID. This ID is what the game server must distribute to connecting clients.

## 3. Server delivery with MultiAddonManager

Install Source2ZE MultiAddonManager on the server and add the JBForsaken UI Workshop ID as a client-only addon in:

```text
game/csgo/cfg/multiaddonmanager/multiaddonmanager.cfg
```

Example:

```cfg
mm_client_extra_addons "YOUR_JBFORSAKEN_UI_WORKSHOP_ID"
```

If other client-side addons are already present, use comma-separated IDs.

Changes to `mm_client_extra_addons` affect future connections. Restart/reload as appropriate before testing with a fresh client connection.

## 4. Versioning rule

Whenever `jbf_menu.xml` or `jbf_menu.css` changes:

1. Run `build.ps1`.
2. Update the existing `jbforsaken_ui` Workshop item.
3. Restart the test client because Panorama layouts are cached for the session.
4. Verify at least one real menu such as Shop/Achievements before deployment.

Do not create a new Workshop item for every UI version; keep one stable Workshop ID and update its content.
