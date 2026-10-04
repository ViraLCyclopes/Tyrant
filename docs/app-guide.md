# Tyrant — app guide

Tyrant is a modding toolkit for Prehistoric Kingdom. The app wraps the `tyrant` command-line tool: everything it does
happens in a workspace folder you choose, never in the game folder (the only exception is the optional data dumper,
which you install and remove explicitly).

## First run

1. Start Tyrant. It opens on **Home** and finds Prehistoric Kingdom through Steam. If it cannot, click
   **Find the game folder…** on Home and pick the folder that contains `Prehistoric Kingdom.exe`.
2. Click **New workspace…** on Home and choose an empty folder outside the game folder (for example `D:\Tyrant\workspace`).
3. Open the **Workspace** tab (its button in the dock at the bottom of Home) and click **Refresh all**. Tyrant
   decompiles the game code into `source/`, indexes the game's assets and, if the dumper is installed, dumps the game
   data. Progress shows in the status line at the bottom; **Cancel** stops the current task.

## Home and tabs

Tyrant opens on **Home**: the game's art, a short intro (hide it with **✕ Hide this**; **i** brings it back) and the
**dock** of tools at the bottom. Click a tool to open it in its own tab. Greyed tools are not built yet.

- **Tabs** keep their place while you look at another tab. **Assets** and **Data** can be open more than once (**+**
  opens another Assets tab); the other tools have one tab each.
- **Ctrl+Tab** / **Ctrl+Shift+Tab** switch tabs, **Ctrl+W** closes one, **Ctrl+L** shows the log, **Ctrl+,** opens
  Preferences.
- The **File** menu opens or creates a workspace, lists recent ones and changes the game folder, from any tab.
- Tyrant reopens your tabs next time (turn this off in **Edit ▸ Preferences**).

## The log

Each tab has a log: what that tab did, its warnings and errors, plus Tyrant's own messages. The **Log** button at the
right of the menu bar (or **Ctrl+L**) shows or hides it; it sits at the side, and **Dock at the bottom** in its header
moves it (drag its edge to resize). A dot on the Log button means the tab has a new warning (yellow) or error (red).
The status line at the bottom shows the tab's latest message; click it to open the log. Errors open the log by
themselves, and an error you can fix shows as a banner in its tab with a button that fixes it. A tab whose log got a
warning or error while you were elsewhere shows ⚠ or ✕ on its title. **Help ▸ Show studio.log** finds the full log file.

## Workspace

The game folder, your workspace and its outputs.


- **Outputs** lists what the workspace contains and when it was made. After a game update the outputs are marked
  *stale*; run **Refresh all** again.
- **Tyrant in the game** installs MelonLoader (the community's mod loader, version 0.7.3, checksum-verified) and two
  small Tyrant mods: the data dumper, which does nothing during normal play, and the modding framework, which applies
  the mods you install from the Mods tab. **Run data dump** starts the game through Steam, reads every game database at
  the main menu and closes the game again (about a minute). **Uninstall from game** removes exactly what was installed,
  including installed mods; MelonLoader stays if other mods use it.
- **Copy diagnostics** (also in the **Help** menu) puts versions, the game build and recent log lines (never game files) on the clipboard for
  bug reports.

## Assets

Needs an asset index (Workspace → **Index assets**: the first run takes about two minutes, later runs only read the
files that changed; again after a game update).

- **Browse**: the left column groups bundles the way the game does (one group per animal, plus shared and UI groups);
  ▸ opens a group's bundles. Search matches names, Addressables paths, GUIDs, script names and bundles; **Type**
  narrows to one kind (Texture2D, Mesh, GameObject = prefab, MonoBehaviour = game data, …).
- **Built-in** groups (`Built-in · sharedassets0`, `resources`, `level…`) are the game's own files outside the bundles:
  fences, paths, buildings, scenery and their textures. They browse, preview, export and replace like everything else.
- Click an asset to see its **keys** (Addressables path, GUID and reference, each with a copy button — replacement mods
  point at these), its size, what it **references** (click to follow) and all of its **fields**.
- **Preview**: textures show at once, with **R/G/B/Alpha** views for packed maps. Meshes and prefabs show in 3D after
  **Load 3D preview**, textured like the game (turn **Textures** off for plain shading).
  - **Skin** switches between the species' diffuse textures. In game, genetics tint them; the preview shows them untinted.
  - **Ground** and **Sky** use the game's own grass, dirt and sand textures and sky cubemaps. The choice is remembered.
  - Navigate as in Blender: middle-drag (or left-drag) orbits, Shift-drag pans, the wheel or Ctrl-drag zooms. Numpad
    1/3/7 view the front, right side and top (Ctrl: the opposite side), 5 toggles orthographic, and . or Home (or
    **Frame**) centres the model. Click the view first so it gets the keys.
  - **Show skeleton** draws the bones of animated models.
  - If the preview says textures need a newer asset index, click **Index assets** on the Workspace tab once.
  - Previews are cached in `<workspace>\cache\previews` per game build and made again after a tool update.
- **Select all** ticks every asset the search and filters match (all pages), **Clear** unticks them, and **Export
  group** exports every asset of the group picked on the left. **Export selected** counts only ticked assets the
  current search shows.
- Tick assets and click **Export selected**: textures become PNG (`assets\textures`), meshes and prefabs .glb
  (`assets\models`), anything else JSON (`assets\json`). A report in `exports\` lists every asset, its keys and any
  failure; one failure never stops the rest. Models are written with their materials; their textures go to a
  `textures` folder next to the `.glb` files, which Blender picks up on import. A bare Mesh is exported without its
  bones; export its prefab (GameObject) to keep them. Very long names are shortened with a short code at the end.
- **Species**: export one animal's pack — its models, every texture of its group and `targets.json` with each asset's
  key — into `assets\species\<name>`.

## Mods

- **Make a texture mod:**
  1. Open a texture in the Assets tab, click **Export selected** (or use the exported PNG under `assets\textures`) and edit it.
  2. Click **Replace in a mod…** on that texture (an animal's, or any object's: fences, buildings, scenery): pick a mod (or **New mod…**), leave **Your PNG** empty to use your edited export (or **Browse…**), then **Add to mod**.
- **Edit a mod:** **Open** a mod in the Mods tab (a new mod opens by itself) to edit it in its own tab.
  - The list on the left has **Mod details**, each **skin**, each **texture replacement** and **Check**; the page on the right edits what you picked.
  - Every change is saved at once. **Ctrl+Z** / **Ctrl+Y** (or **Edit ▸ Undo / Redo**) step back and forward. Replacing or deleting a file cannot be undone.
  - **A skin:** rename it (its id stays, so saved animals keep it), pick a swatch, and for each sex replace a texture with your PNG (**Replace…**) or go back to the base skin's (**Use base**).
  - **Colours** sets the pattern colours of normal animals and the colours of albino, melanistic and leucistic ones, with a 2D preview of six random animals (approximate: no lighting). Pick one colour or a gradient, and a range or a **Fixed** value; **From base skin** leaves a field to the base skin, and **Exact colours** turns off the random tint.
  - **Remove skin…** asks first and can delete its files; its number in the game stays reserved.
  - The **Mod** menu has Install, Check, Restore cutouts, Open folder and Remove from game.
  - If the mod's `mod.json` is changed elsewhere (another program, `tyrant mod …`), the tab reloads it when you come back.
- **Mods tab:**
  - **Check** lists problems: errors stop an install, warnings don't.
  - **Install to game** copies the mod into the game. The first time, it also installs MelonLoader and Tyrant's framework, after asking.
  - **On** switches a mod for the next game start. **Remove from game** takes it out again.
  - *Changed since install* means you edited the workspace copy; install again to update the game.
- **Add a skin:**
  1. On the Species tab, click **Add a skin…** for a species.
  2. Fill in the form:
     - pick a mod (or **New mod…**) and name the skin;
     - choose the vanilla skin to start from and which sexes;
     - optionally tick **Also normal, extra and pattern maps**.
  3. Tyrant copies that skin's textures into `mods\<id>\skins\<skin>\`. Edit them, then **Check** and **Install to game**.
  4. In the game's Nursery the new skin appears next to the vanilla ones, and the row scrolls when there are many.
  - This needs a data dump (Workspace → **Run data dump**).
- **Clean up skin numbers…** lists skins whose mods are gone; forget the ones you won't reinstall.
- **Restore cutouts** appears under a Check result when a colour PNG lost the see-through feathers or hair of its vanilla texture (an editor saved it without alpha). It copies the vanilla transparency back; install again afterwards.
- **In the game's Nursery,** a grid button next to **Design** (species with more than 5 skins) opens **All skins**: every skin with a search box (skin or mod name). Click a skin to wear it; close with the X or Esc.
- **Update Tyrant in game** (Workspace tab) appears after a Tyrant update; installing a mod also updates the framework.
- **Start the game.** `MelonLoader\Latest.log` lists the mods Tyrant loaded and each texture it replaced.
- **The file format** is described in `docs/mod-format.md`.

## Data

- **Tables**: every dumped type as a table (species are `AnimalData`). Click a column header to sort, type in the
  filter box to narrow rows (every word must match somewhere), choose columns with **Columns**, tick rows and click
  **Compare** to see them side by side, and export the whole type with **Export CSV** / **Export JSON**.
- **Browse**: pick a type and an object to see all of its fields as a tree; references to other assets show as
  `→ name (type)`.
- **Localization**: every in-game text in every language; tick the languages to show and search terms or text.

## When something goes wrong

- Errors appear in a red banner in the tab that caused them, with a button that fixes the usual cause (pick the game
  folder, pick a workspace, install the dumper, refresh outputs). Other errors and warnings go to the tab's log, which
  opens by itself when an error arrives.
- If the background core stops, Tyrant restarts it (up to three times a minute) and reopens your workspace.
- Logs: `<workspace>\logs\studio.log` (what Tyrant did in that workspace) and
  `%LOCALAPPDATA%\com.tyrant.toolkit\logs\core.log` (the core's own log). The dumper's log is
  `<game folder>\MelonLoader\Latest.log`.
