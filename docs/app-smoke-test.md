# Tyrant app — manual smoke test

Run before each release, on a machine with Prehistoric Kingdom installed through Steam. Steps marked **(game)**
change the game folder or start the game.

Development build: `dotnet build Tyrant.slnx`, then in `studio/`: `npm run tauri dev`.
Release build: `npm run build:app`, install `studio/src-tauri/target/release/bundle/nsis/Tyrant_0.1.0_x64-setup.exe`.

1. The window opens on Home: the two-sauropod art, the intro card and the dock. Open **Workspace** from the dock: it
   shows the detected game folder, its build and Steam app 666150.
2. **New workspace…** in an empty folder: the workspace path and an empty Outputs table appear.
   **Show in Explorer** opens it. Closing and reopening the app reopens the same workspace.
3. Pick a folder inside the game folder as a new workspace: a red error explains why and offers **Pick workspace folder**.
4. **Decompile code**: the status line shows progress and the Workspace tab's log (Ctrl+L) lists each step; Outputs lists six `Code:` rows marked current.
5. **Index assets**: completes with an asset count in the tab's log.
6. **(game)** **Install dumper**: the status becomes *Installed*.
7. **(game)** **Run data dump**: confirm the prompt; the game starts and closes by itself; the log reports
   objects, types and languages; Outputs shows *Game data*.
8. Data → Tables: AnimalData opens first; sort by a numeric column (numbers sort by value); filter `herbivore`;
   choose columns; tick two species and **Compare**; **Only differences** toggles.
9. **Export CSV**, open it in Excel: accented names (e.g. French text) display correctly.
10. Data → Browse: pick a type and an object; nested fields expand; **Copy JSON** works.
11. Data → Localization: English shows; ticking another language adds a column; searching finds a term.
12. End `tyrant.exe` in Task Manager: a red bar says the core restarted; the workspace is open again and
    the Workspace tab still shows its status.
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
    If the preview asks for a newer asset index, click **Index assets** on the Workspace tab and load it again.
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
29. **(game)** Workspace → **Run data dump** (once). Species → Carcharodontosaurus → **Add a skin…** → name `Red spot`, base `Alt 1`, Male only,
    mod `red-spot-carcharo` → **Add skin**: `mods\red-spot-carcharo\skins\red-spot\male_D.png` exists. Paint on it.
30. Mods → **Check** (no errors) → **Install to game**.
31. **(game)** Start the game → Nursery → Carcharodontosaurus → male: a new swatch "Red spot" is next to the vanilla ones; choosing it shows
    your texture; vanilla skins are unchanged. `MelonLoader\Latest.log` has `Carcharodontosaurus: 1 skin(s) added.` and
    `skin red-spot-carcharo/red-spot (male) on …`. The swatch sits right after the vanilla ones (no gap where the 12 hidden
    reserved positions are), and no "(skin from a removed mod)" swatch shows.
32. **(game)** Place the animal, save, quit, load: it still wears Red spot.
33. **(game)** Close the game, **Remove from game** (now no installed mod adds skins), start and load: the park loads; the animal
    shows the first vanilla skin; the log still has `Carcharodontosaurus: 0 skin(s) added, 1 kept as stand-ins for removed mods`.
    Save, then reinstall: Red spot is back (the stand-in kept its number).
34. **(game)** With 6+ skins for one species (add more skins), the Nursery skin row scrolls sideways.
35. Mods → **Clean up skin numbers…** after removing a skin mod: it is listed; **Forget selected** (confirm) clears it.
36. Edit `mods\blue-green-stripes\mod.json` by hand (no editor before the GUI overhaul): add to the skin
    `"colors": { "tint": { "hue": 0, "saturation": 0, "value": 0 } }`, then **Check** and **Install to game**.
37. **(game)** Nursery → Carcharodontosaurus → Blue-green stripes: generate several animals; the stripes keep their exact colours (no dull ones).
38. In `mods\test-skins\mod.json`, give Purple spots `"colors": { "pattern": { "a": "#ffe000", "b": "#ff6000", "strength": 0.8 } }`;
     Check, Install. **(game)** Normal animals of Purple spots show yellow to orange pattern colours where the pattern map is red.
39. **(game)** Place one, save, quit, load: same colours. `Latest.log` has `Skin pattern colours were kept for a loaded or bred animal.`
40. **(game)** Close the game, remove `test-skins`, start and load: the park loads; that animal looks like the first vanilla skin.
41. Workspace → **Index assets** twice: the second run is much faster (only changed files are read). Assets lists
    `Built-in · sharedassets…` groups; a fence texture (search `fence`) previews and exports to a PNG.
42. Assets → **Select all** with a search typed: **Export selected (N)** matches the result count; **Clear** sets it to 0.
    Pick a group, **Export group**: the notice reports the group's asset count.
43. **(game)** Fence texture → **Replace in a mod…** → new mod with a recoloured PNG → **Install to game**. Start the game,
    place that fence: the new texture shows on the fence and in the build menu. `MelonLoader\Latest.log` has
    `Replaced … on game materials`.
44. **Copy diagnostics** and paste: versions, build, workspace and log tails appear.
45. **(game)** **Uninstall from game**: the notice says the game folder is back to vanilla; `version.dll`,
    `MelonLoader\`, `Mods\`, `UserData\` and `UserLibs\` are gone from the game folder (if Tyrant installed them).
46. Release build only: the installed app starts, finds the core next to itself (step 1 works) and installs the
    dumper from its own `sidecar\dumper` folder.
47. Assets → Acrocanthosaurus prefab → **Load 3D preview**: feather/hair edges are cut out (no solid sheets), the body is
    not shiny metal, the eyes look wet. A fence prefab shows its own texture.
48. Mods → open a skin mod on Stegosaurus → the skin's files show no **fur** row; on Carcharodontosaurus no **pattern** row.
49. Colours → set Colour A red, Colour B blue: the 3D animal and the first 2D animal show the same colours; **↻** changes
    both; **Infant** shows the infant textures; switching to another tab and back keeps the view (it paused while hidden).
50. In a test workspace without `data\`, open the mod editor's Colours: the 3D area says to Run data dump; the 2D strip
    still draws.
51. Assets → Carcharodontosaurus prefab → **Export selected** → open the LOD00 .glb in Blender 5.2 (plain bones) → sculpt the head bigger in
    Sculpt Mode with the Basis selected in Shape Keys (the growth keys follow; do not sculpt them) → File → Export → glTF 2.0 (.glb).
52. Assets → the prefab → **Replace model in a mod…** → new mod `big-head-carch` → Browse… your .glb → **Add to mod**: the notice names
    3 LODs. Mods → open it: Models → Carcharodontosaurus shows the big head; LOD 1 and LOD 2 show simpler versions; the numbers sit near
    the game's.
53. **(game)** Install, start the game, place a baby Carcharodontosaurus: big head; it grows into an adult with the big head; it walks
    normally (feet on the ground) and wears its skin. `MelonLoader\Latest.log` has "Replaced Carcharodontosaurus's model".
54. Give a skin its own model (skin page → Model → Replace model…): in the Nursery that skin shows its model, other skins the species
    model; switching an animal back to another skin puts that skin's model (or the game's) back.
55. Export the .glb from step 51 again from Blender: the model's page says it changed since you added it; **Re-import** brings in the
    new version (Ctrl+Z goes back).
56. Workspace → Tyrant in the game shows "Framework: 0.4.0 in the game · 0.4.0 in this Tyrant" and MelonLoader's version.
57. Mod editor → Mod → **Export for sharing…** on a skin mod: the zip holds `UserData\Tyrant\Mods\<id>\…` and `README.txt`;
    older model builds are not inside.
58. In a second workspace: Mods → **Add mod from zip…** → the mod appears; doing it again asks to replace it.
59. Workspace → **Save framework zip…**: the zip holds `Mods\Tyrant.Framework.dll`, `UserLibs\…` and `README.txt`.
60. **(game)** With Tyrant uninstalled from the game, install MelonLoader by hand, unzip the framework zip and the mod zip
    into the game folder and start the game: the mod shows (MelonLoader's log lists it).
61. A build without `studio/src-tauri/updater-key.pub` content: Help → Check for updates says updates aren't set up in this
    build; Tyrant keeps working.
62. Edit → Preferences → untick **Check for updates when Tyrant starts**, restart: no update check runs.
63. **(after the first signed release)** An older installed Tyrant shows "Tyrant X is available"; **Update now** installs it
    and Tyrant restarts on the new version; the framework offer follows when its framework changed.
64. **(game)** Workspace → **Run data dump** once more: `<workspace>\data\audio\events.json` appears; Species tab →
    **All sounds…** finds "click" and music without the "only the animals' sounds" note.
65. **(game)** Species → Carcharodontosaurus → **Sounds…** → Calls → **Replace…** on the social call (*shared with N species*),
    pick two short OGGs, keep **Only Carcharodontosaurus**, add to a mod and install. In the game, a Carcharodontosaurus calls
    with your files (one at random) while an Acrocanthosaurus beside it keeps the game's call, also when both call together.
    `MelonLoader\Latest.log` has "Replaced event:/… for Carcharodontosaurus".
66. **(game)** All sounds → a button click → **Replace…** (for everyone) → install: every click in the game's menus plays your
    file.
67. **(game)** Lower the game's Sounds slider: the replaced calls and clicks get quieter with it, down to silent at 0.
68. **(game)** A baby Carcharodontosaurus calls higher than an adult; set **Baby pitch** to 0 on the sound's page, install: the
    baby sounds like the adult.
69. **(game)** Replace the large-theropod breathing (Breathing group, a looping sound): it loops while the animal breathes and
    stops when it stops (or when the animal is removed).
70. **(game)** Delete one of the mod's sound files from `<game>\UserData\Tyrant\Mods\<id>\sounds\`, start the game: that sound
    plays the game's original and `Latest.log` says which file could not be opened; Tyrant's Check on the workspace copy is
    still clean (the workspace file is there).

**Shell (tabs, log, preferences):**

- Open Assets twice (dock, then **+**): two tabs, the second titled *Assets (2)*. Filter one; switch away and back: the
  filter is still there. **Ctrl+Tab** cycles; **Ctrl+W** closes; Home has no close button.
- Start an asset export in one Assets tab and switch to Home: when it finishes, the Assets tab shows ⚠ if it had warnings.
- **Ctrl+L** opens the log; drag its edge to resize; **View ▸ Log at the side** moves it; **Clear** empties only that tab.
- **Edit ▸ Preferences**: untick *Show the intro on Home* (the card goes; **i** brings it back); untick *Reopen my tabs on
  start*, restart: only Home opens. Tick it again, open a few tabs, restart: they come back.
- **File ▸ Open workspace…** from the Data tab opens another workspace; **Help ▸ Show studio.log** reveals the log file;
  **Help ▸ About Tyrant** shows the version.
- Narrow the window to about 700 px: the tab strip scrolls, the intro and dock still fit.

**Mod editor:**

- Mods tab → **Open** a mod with skins: it opens in its own tab named after the mod; the side list shows Mod details, the skins, the replacements and Check (with a ⚠ count when Check found something).
- Rename a skin: the side list updates; **Ctrl+Z** puts the old name back, **Ctrl+Y** redoes it. Ctrl+Z inside a text field undoes the typing only.
- Replace a skin's male normal with a PNG: its thumbnail appears; **Use base** clears it (refused for the skin's last file).
- **Colours** on Normal: untick *From base skin* on Colour A, pick a colour: the preview strip shows six animals in it; **↻** shows six others. Switch to **Albino** and set **Eyes**.
- Edit the mod's `mod.json` in a text editor, switch to another tab and back: the editor reloads it and the log says so.
- **Remove skin…** with *Also delete its files*: the skin and its PNGs go; `tyrant mod show <id>` lists what is left.
