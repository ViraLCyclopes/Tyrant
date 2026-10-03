# Tyrant app — manual smoke test

Run before each release, on a machine with Prehistoric Kingdom installed through Steam. Steps marked **(game)**
change the game folder or start the game.

Development build: `dotnet build Tyrant.slnx`, then in `studio/`: `npm run tauri dev`.
Release build: `npm run build:app`, install `studio/src-tauri/target/release/bundle/nsis/Tyrant_0.1.0_x64-setup.exe`.

1. The window opens; Home shows the detected game folder, its build and Steam app 666150.
2. **New workspace…** in an empty folder: the workspace path and an empty Outputs table appear.
   **Show in Explorer** opens it. Closing and reopening the app reopens the same workspace.
3. Pick a folder inside the game folder as a new workspace: a red error explains why and offers **Pick workspace folder**.
4. **Decompile code**: the top bar shows progress; Outputs lists six `Code:` rows marked current.
5. **Index assets**: completes with an asset count in the green notice.
6. **(game)** **Install dumper**: the status becomes *Installed*.
7. **(game)** **Run data dump**: confirm the prompt; the game starts and closes by itself; the notice reports
   objects, types and languages; Outputs shows *Game data*.
8. Data → Tables: AnimalData opens first; sort by a numeric column (numbers sort by value); filter `herbivore`;
   choose columns; tick two species and **Compare**; **Only differences** toggles.
9. **Export CSV**, open it in Excel: accented names (e.g. French text) display correctly.
10. Data → Browse: pick a type and an object; nested fields expand; **Copy JSON** works.
11. Data → Localization: English shows; ticking another language adds a column; searching finds a term.
12. End `tyrant.exe` in Task Manager: a red bar says the core restarted; the workspace is open again and
    Home still shows its status.
13. Start **Run data dump** and close the app while the game is starting: `tyrant.exe` exits within a few seconds
    and `<game>\UserData\tyrant.dumper.request.json` does not exist.
14. **Copy diagnostics** and paste: versions, build, workspace and log tails appear.
15. **(game)** **Uninstall dumper**: the notice says the game folder is back to vanilla; `version.dll`,
    `MelonLoader\`, `Mods\`, `UserData\` and `UserLibs\` are gone from the game folder (if Tyrant installed them).
16. Release build only: the installed app starts, finds the core next to itself (step 1 works) and installs the
    dumper from its own `sidecar\dumper` folder.
