# Tyrant — app guide

Tyrant is a modding toolkit for Prehistoric Kingdom. The app wraps the `tyrant` command-line tool: everything it does
happens in a workspace folder you choose, never in the game folder (the only exception is the optional data dumper,
which you install and remove explicitly).

## First run

1. Start Tyrant. It finds Prehistoric Kingdom through Steam. If it cannot, click **Change game folder…** and pick
   the folder that contains `Prehistoric Kingdom.exe`.
2. Click **New workspace…** and choose an empty folder outside the game folder (for example `D:\Tyrant\workspace`).
3. Click **Refresh all**. Tyrant decompiles the game code into `source/`, indexes the game's assets and, if the
   dumper is installed, dumps the game data. Progress shows in the top bar; **Cancel** stops the current task.

## Home

- **Outputs** lists what the workspace contains and when it was made. After a game update the outputs are marked
  *stale*; run **Refresh all** again.
- **Game data dumper** installs MelonLoader (the community's mod loader, version 0.7.3, checksum-verified) and a
  small dumper mod. The mod does nothing during normal play. **Run data dump** starts the game through Steam, reads
  every game database at the main menu and closes the game again (about a minute). **Uninstall dumper** removes
  exactly what was installed; MelonLoader stays if other mods use it.
- **Copy diagnostics** puts versions, the game build and recent log lines (never game files) on the clipboard for
  bug reports.

## Assets

Needs an asset index (Home → **Index assets**, about 20 seconds; again after a game update).

- **Browse**: the left column groups bundles the way the game does (one group per animal, plus shared and UI groups);
  ▸ opens a group's bundles. Search matches names, Addressables paths, GUIDs, script names and bundles; **Type**
  narrows to one kind (Texture2D, Mesh, GameObject = prefab, MonoBehaviour = game data, …).
- Click an asset to see its **keys** (Addressables path, GUID and reference, each with a copy button — replacement mods
  point at these), its size, what it **references** (click to follow) and all of its **fields**.
- **Preview**: textures show at once, with **R/G/B/Alpha** views for packed maps; meshes and prefabs show in 3D after
  **Load 3D preview** (drag to orbit, scroll to zoom; **Show skeleton** for animated models). Previews are cached in
  `<workspace>\cache\previews` per game build.
- Tick assets and click **Export selected**: textures become PNG (`assets\textures`), meshes and prefabs .glb
  (`assets\models`), anything else JSON (`assets\json`). A report in `exports\` lists every asset, its keys and any
  failure; one failure never stops the rest.
- **Species**: export one animal's pack — its models, every texture of its group and `targets.json` with each asset's
  key — into `assets\species\<name>`.

## Data

- **Tables**: every dumped type as a table (species are `AnimalData`). Click a column header to sort, type in the
  filter box to narrow rows (every word must match somewhere), choose columns with **Columns**, tick rows and click
  **Compare** to see them side by side, and export the whole type with **Export CSV** / **Export JSON**.
- **Browse**: pick a type and an object to see all of its fields as a tree; references to other assets show as
  `→ name (type)`.
- **Localization**: every in-game text in every language; tick the languages to show and search terms or text.

## When something goes wrong

- Errors appear in a red bar with a button that fixes the usual cause (pick the game folder, pick a workspace,
  install the dumper, refresh outputs).
- If the background core stops, Tyrant restarts it (up to three times a minute) and reopens your workspace.
- Logs: `<workspace>\logs\studio.log` (what Tyrant did in that workspace) and
  `%LOCALAPPDATA%\com.tyrant.toolkit\logs\core.log` (the core's own log). The dumper's log is
  `<game folder>\MelonLoader\Latest.log`.
