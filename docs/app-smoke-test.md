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
14. Assets: with the asset index built, the Assets tab lists groups and assets; searching `carcharo` finds the
   Carcharodontosaurus textures and prefab.
15. Click a skin texture (`..._D`): the PNG preview appears; R/G/B/Alpha change the view; **Copy Addressables path**
   puts the path on the clipboard.
16. Click the Carcharodontosaurus prefab and **Load 3D preview**: the animal appears, can be orbited, and
   **Show skeleton** draws its bones.
17. Tick two textures and the prefab, **Export selected**: the notice reports 3 exported and the report path;
   `assets\textures` holds the PNGs and `assets\models\<prefab>` the .glb files.
18. Species → find Carcharodontosaurus → **Export pack** → **Show in Explorer** opens `assets\species\carcharodontosaurus`
   with `models\`, the textures and `targets.json`.
19. Assets → filter `Acrocanthosaurus`, select the prefab (GameObject), **Load 3D preview**. The animal is textured
    (not dark/metallic), stands on grass with a shadow under its feet, and a blue sky with the horizon below is behind it.
    If the preview asks for a newer asset index, click **Index assets** on Home and load it again.
20. Middle-drag orbits, Shift+middle-drag pans, the wheel zooms without scrolling the panel. Click the view, then press
    numpad 1: the head faces you. Numpad 3: the animal's right side. Numpad 7: from above, head towards the bottom.
    Numpad 5: orthographic. Numpad . (or Home): the animal is centred again.
21. **Skin** lists the species' `_D` textures with the worn one selected; choosing another changes the body, choosing
    the first again puts it back. Untick **Textures**: plain shading; tick it: textures return.
22. **Ground** → Desert, **Sky** → Evening: both change. Close and reopen the app, preview a prefab: Desert and
    Evening are still chosen. Set them to None: the plain dark-green background, no floor.
23. Tick the prefab, **Export selected**, open the `.glb` in Blender 5.2 (File → Import → glTF): the model is textured,
    and `assets/models/<name>/textures/` holds the PNGs.
24. Assets → open `T_carcharodontosaurus_alt1_male_D` → **Replace in a mod…** → New mod `red-spot-carcharo` → leave Your PNG empty
    (after exporting and editing it) → **Add to mod**: the notice says to install it from the Mods tab.
25. Mods: the mod shows *Not installed* with 1 texture; **Check** says no problems (or warns about size if it differs).
26. **(game)** **Install to game** → confirm: the notice says it was installed; the row shows *Installed* and **On** is ticked;
    `<game>\UserData\Tyrant\Mods\red-spot-carcharo\` exists and `<game>\Mods\Tyrant.Framework.dll` is present.
27. **(game)** Start the game, spawn a male Carcharodontosaurus with the alt1 skin: the red spots show. `MelonLoader\Latest.log` has
    `red-spot-carcharo 1.0.0: 1 texture replacement(s)` and `replaced T_carcharodontosaurus_alt1_male_D (_AdultDiffuse) on …`.
28. **(game)** Close the game, untick **On**, start again: vanilla skin. **Remove from game**: the folder is gone.
29. **(game)** Home → **Run data dump** (once). Species → Carcharodontosaurus → **Add a skin…** → name `Red spot`, base `Alt 1`, Male only,
    mod `red-spot-carcharo` → **Add skin**: `mods\red-spot-carcharo\skins\red-spot\male_D.png` exists. Paint on it.
30. Mods → **Check** (no errors) → **Install to game**.
31. **(game)** Start the game → Nursery → Carcharodontosaurus → male: a new swatch "Red spot" is next to the vanilla ones; choosing it shows
    your texture; vanilla skins are unchanged. `MelonLoader\Latest.log` has `Carcharodontosaurus: 1 skin(s) added.` and
    `skin red-spot-carcharo/red-spot (male) on …`.
32. **(game)** Place the animal, save, quit, load: it still wears Red spot.
33. **(game)** Close the game, **Remove from game**, start and load: the park loads; the animal shows the first vanilla skin. Reinstall:
    Red spot is back.
34. **(game)** With 6+ skins for one species (add more skins), the Nursery skin row scrolls sideways.
35. Mods → **Clean up skin numbers…** after removing a skin mod: it is listed; **Forget selected** (confirm) clears it.
36. **Copy diagnostics** and paste: versions, build, workspace and log tails appear.
37. **(game)** **Uninstall from game**: the notice says the game folder is back to vanilla; `version.dll`,
    `MelonLoader\`, `Mods\`, `UserData\` and `UserLibs\` are gone from the game folder (if Tyrant installed them).
38. Release build only: the installed app starts, finds the core next to itself (step 1 works) and installs the
    dumper from its own `sidecar\dumper` folder.
