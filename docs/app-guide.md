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
