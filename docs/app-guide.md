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
  The card shows the framework's version in the game and in this Tyrant ("Framework: 0.1.0 in the game · 0.1.0 in this
  Tyrant") and MelonLoader's version. After a Tyrant update that brings a newer framework, Tyrant offers once to
  **Update Tyrant in game**. **Save framework zip…** saves the framework for players without Tyrant.
  CLI: `tyrant game status|install|update|uninstall|package-framework`.
- **Updates:** Tyrant checks GitHub for a new version once a day (**Edit → Preferences → Check for updates when Tyrant
  starts**). A banner offers **Update now** (downloads, checks the signature, installs and restarts; it waits while a job
  runs), **What's new** (the release page) or **Later**. **Help → Check for updates** checks any time. CLI: `tyrant update check`.
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
  - Animals are drawn with the game's animal shader rules: cut-out feathers and hair, ambient occlusion and smoothness from
    the extra map, eye colour and pattern colours. Other models use their own textures. The look is close, not exact
    (lighting, rim and fur are approximated).
  - If one part of a model cannot be shown, the others are, with a note.
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
  `textures` folder next to the `.glb` files, which Blender picks up on import. Animals export with their cutouts (feathers,
  hair) and with ambient occlusion and roughness from the extra map (`<extra>_ORM.png`); their pattern map is exported next
  to them too, but pattern colours need a Blender setup (planned). A bare Mesh is exported without its
  bones; export its prefab (GameObject) to keep them. Very long names are shortened with a short code at the end.
  For plain bones in Blender (no "Icosphere"): in **File → Import → glTF 2.0**, open **Bones & Skin** and tick **Disable Bone
  Shape**, or set **Bone Dir** to **Temperance** (bones then point at their children). The Icosphere is a bone display shape
  Blender's importer adds by default; it is not in Tyrant's files. Save the import settings as an operator preset (the
  preset menu at the top of the import dialog) so Blender remembers them.
  Also tick **Merge Vertices** in the same dialog: a glTF file stores a vertex once per hard edge and UV seam, and without
  merging Blender leaves the mesh cut along every one of them (Seams from Islands, Tris to Quads and UV selection then go
  wrong). Merged, **Seams from Islands** marks just the real UV seams and **Tris to Quads** (Alt+J) rebuilds clean quads.
  Tyrant accepts the merged and quad mesh back as it is.
- **Species**: export one animal's pack — its models, every texture of its group and `targets.json` with each asset's
  key — into `assets\species\<name>`.
- **Sounds** (Species tab; needs a data dump): **Sounds…** on a species lists its sounds by moment (calls, growls,
  footsteps, breathing, eating and drinking, body, combat). A sound marked *shared with N species* plays for all of them.
  **All sounds…** searches every game sound, buttons, buildings and music included (a dump made before Tyrant 0.4 lists
  only the animals' sounds; run the data dump again).

## Mods

- **Make a texture mod:**
  1. Open a texture in the Assets tab, click **Export selected** (or use the exported PNG under `assets\textures`) and edit it.
  2. Click **Replace in a mod…** on that texture (an animal's, or any object's: fences, buildings, scenery): pick a mod (or **New mod…**), leave **Your PNG** empty to use your edited export (or **Browse…**), then **Add to mod**.
- **Replace a model:**
  1. Assets tab → the animal's prefab (GameObject) → **Export selected**, and open the LOD00 `.glb` in Blender (for plain bones see the export notes in the Assets section).
  2. Reshape or replace the mesh; keep the armature, its bone names, the two growth shape keys and the material names. Sculpt or edit with the Basis selected in Shape Keys: the growth keys follow it, so do not repeat the change on them. Do not use Voxel Remesh (it deletes the shape keys). File → Export → glTF 2.0 (.glb).
  3. Assets tab → the prefab → **Replace model in a mod…** → pick a mod (or **New mod…**) → **Browse…** your `.glb` → **Add to mod**. Tyrant checks it, builds its levels of detail and adds it. For one skin only, use the skin's **Model** row in the mod editor.
  4. CLI: `tyrant mod replace-model <mod> <species> <file.glb> [--skin <id>]`, `tyrant mod remove-model`, `tyrant mod rebuild-models`.
- **Change an animal's size:**
  1. Mod editor → **Sizes +** → pick the species (the game's or your mod's) → **Open**. A mod's own species also has the card under **Size** on its species page.
  2. Four fields, each a number and a slider: **Adult size ×** (0.05 to 5), **Baby size (share of adult)** (above 0, at most 1), **Male size ×** and **Female size ×** (0.1 to 5). The number fields take up to 20 (Check says whether the result fits the size limit); the sliders go to 5, or to the size limit (step 4) when it is higher. An unset Adult or Baby field shows the game's value; **↺** puts it back. Male and Female multiply each skin's own sex size, so unset (and **↺**) is 1×; the skin's own sizes show below them, for example `game: 1.08 male / 1.12 female`.
  3. The size bar shows the original and new sizes, as lengths when the game's model length is known (for example 12.1 m → 10.3 m). Problems Check finds show under the fields, for instance a species scaled outside the size limit (the game's 0.05 to 2 unless the mod sets one), or one that can grow past the game's normal 2× within a raised limit. A mod's own species whose record still changes its growth curve or sex sizes (edits made in the field editor before Size existed, which it no longer shows) lists those edits on the card; **Remove old size edits** takes them out, so only Size changes its size. That step cannot be undone.
  4. **Mod details** has **Nursery size slider range** (the game's own Customize size slider in the Nursery covers this range; random eggs keep the game's range) and **Breeding size range**. Leave **Game's range** ticked to keep the game's, or untick it to set your own (the smallest value cannot pass the largest; both reach 20). **Size limit** is the smallest and largest size an animal may have in game: the game caps animals at 0.05×–2×. Untick **Game's limit** to widen it: **Minimum** takes 0.01 to 0.05 and **Maximum** 2 to 20 (a number outside those is not saved). With several mods on, the widest limit wins. Sizes past 2× then show in game, but the game was tuned for normal sizes, so very big animals may clip, get stuck or frame badly. For an uncapped Nursery slider, set the limit to 0.05–20 and the Nursery range to, for example, 0.25–5.
  5. Changes can be undone. CLI: `tyrant mod size <mod> <species>` (`--adult`, `--baby`, `--male`, `--female`, `--clear`, `--remove-record-edits`) and `tyrant mod size-ranges <mod>` (`--nursery`, `--breeding`, `--limit`, `--clear`; each range as `min,max`, for example `--limit 0.05,20`).
  6. In Blender (Tyrant panel, N → Tyrant): the **Growth** slider shows the animal's real size from baby to adult, for the sex shown, by scaling the armature object. That scale is only for viewing: **Send to Tyrant** exports the model at the object's own scale. The **Size** box has the same four fields; when the model goes to a species (the game's species model or a mod's own species) a change shows at once, and **Send to Tyrant** writes it to the mod. Tyrant takes that as a change made outside the app: it reloads the mod and clears its undo history, so Undo cannot take it back. To go back, set the field again in Blender and send, or use **↺** on the Size card. For a skin the fields are read-only: sizes apply to the whole species, so set them in the app. The size shown keeps to the mod's size limit as the game does (the fields themselves go up to 20).
- **Replace a sound:**
  1. Species tab → **Sounds…** on the animal (or **All sounds…**) → **Replace…** on the sound.
  2. Pick a mod (or **New mod…**) and **Choose files…**: WAV, OGG, MP3 or FLAC; pick several and one plays at random each time.
  3. From a species' list choose **Only <species>** (the default: other animals that share the sound keep the game's) or **For every animal that uses it**. From All sounds it is for everyone.
  4. **Add to mod**, then **Install to game**. Babies play the new sound higher, as they do the game's.
  5. A whole sound pack at once: mod editor → **Sounds +** → pick the species → **Replace from folder…**. Files named like
     the game's sounds match by name, whatever prefix and take number they have (`AlloAnax_VoxAngry_03.wav` is take 3 of
     **Angry**); each sound gets all its takes (one plays at random). Untick what you don't want, pick the sound for a
     name that fits two, choose **Who hears them** (every animal of the species, or only one of its skins), then **Replace N sounds** (one step that **Undo** takes back). Files that match nothing are
     listed and left out. CLI: `tyrant mod replace-sounds <mod> <folder> --species <id> [--skin <key>] [--dry-run]`.
  6. CLI: `tyrant sounds list [--species <id>] [--search <text>]`, `tyrant mod replace-sound <mod> <event> <files…> [--species <id> | --skin <key>] [--volume 0-2] [--age-pitch 0-1]`, `tyrant mod set-sound`, `tyrant mod remove-sound`.
- **Edit a mod:** **Open** a mod in the Mods tab (a new mod opens by itself) to edit it in its own tab.
  - The list on the left has **Mod details**, each **skin**, each **texture replacement**, models, sounds and **Check**; the page on the right edits what you picked.
  - **Texture replacements → + Add:** pick a species to list its skins' textures (adult and baby colour, normal, extra, pattern, fur maps; ones other species use too are marked), or search any game texture; **Replace…** picks your PNG. CLI: `tyrant species textures <species>`, then `tyrant mod replace <mod> <texture> <png>`.
  - Every change is saved at once. **Ctrl+Z** / **Ctrl+Y** (or **Edit ▸ Undo / Redo**) step back and forward. Replacing or deleting a file cannot be undone.
  - **A skin:** rename it (its id stays, so saved animals keep it), pick a swatch, and for each sex replace a texture with your PNG (**Replace…**) or go back to the base skin's (**Use base**).
  - **Colours** sets the pattern colours of normal animals and the colours of albino, melanistic and leucistic ones, with a 2D preview of six random animals (approximate: no lighting). Pick one colour or a gradient, and a range or a **Fixed** value; **From base skin** leaves a field to the base skin, and **Exact colours** turns off the random tint.
  - **Material** (on a skin's page) sets how the animal's material looks, for the animal's own material (named at the top, for example *Material: Titanoboa*):
    - **Iridescence** (tick to turn it on; unticked keeps the game's settings). It starts ticked when the skin's base skin
      shimmers in the game (Microraptor's: its strength reads *game's, 25 %*), and unticking it then saves strength 0. On a
      species page it starts unticked with *Game's (only some skins shimmer)*, since only some of a species' skins do:
      - **Strength**, and under **Per mutation** **Albino / Melanistic / Leucistic** (✕ = same as normal). Strength 0 turns the sheen off, even on species that have it in the game.
      - **Varies between animals**: 15 % by default; each animal's spread is stable and nothing is saved.
      - **Colours**: **Game's**, **Import image** (a ramp PNG), **Gradient** (up to 32 stops) or **Real iridescence (thin film)** with a **Film thickness** slider (starts at 420 nm).
      - **Where**: **Everywhere**, **Game's**, **Import image** or **From a map channel** (extra, pattern or fur, and a channel; **Invert**; **Edit a copy** when the skin only has its base skin's map).
      - **Game's** under **Colours** and **Where**, over a ramp or mask a level below sets (species, starting skin), saves `"game"`: the game's own wins; with nothing below it only clears this level's choice.
      - **Species** under **Colours** and **Where** takes that vanilla species' ramp or mask (pick it in the list beside): saved as `{ "species": "<id>" }`, a reference; the game's image is never copied into the mod (the preview reads it from your game files).
      - **Start from a species…** (above **Strength**; it lists the species whose skins shimmer, from the data dump) fills this level from that species: its ramp and mask (by reference), its shaping and strength, and turns iridescence on. The rest stays, everything can be edited after, and **Ctrl+Z** takes it back. It needs the data dump and the asset index.
      - **Edge falloff**, **Falloff curve**, **Brightness offset**.
    - **Fur** has two mask rows, **Fur mask** and **Baby fur mask** (**Import image** with a PNG; ✕ goes back to the one below; **Game's**, shown when a level below sets a mask, saves `"game"` so the game's own wins; a baby mask left out uses the adult one; one file serves both sexes), and the settings **Fur length**, **Fur droop** (0–2) and **Droop direction** (x, y, z, each −1 to 1). **Start from a species…** (species with fur) turns **Grow fur** on and copies that species' fur length, droop, wind and droop direction; the fur masks stay. **Grow fur on skins without fur** turns the game's fur on for species that have none (it also sets a fur length of 0.5 when nothing gives one, and unticking takes that length back while it is unchanged). When a level below (the species, a starting skin) grows fur, the box shows ticked with a note, and unticking it saves `"grow": false` for this level. Grown fur needs tessellated fur in the game's graphics settings, and the preview here shows no fur, so tune the look in game (the in-game editor's Fur tab has the same **Grow fur** switch).
    - The settings below it (**Surface**, **Detail**, **Rim / fresnel**, **Eyes**, **Fur**, **Cutout**) show what the skin, its base skin, the species or the game gives; type a number or pick a colour to override one, **✕** goes back to the value below. The order is skin, base skin, species, game.
    - The 3D view above previews it live. It approximates the game: the shimmer follows the viewing angle only, and a **Smoothness** override is shown as a multiplier on the extra map's smoothness. Check the real look in the game.
    - Material files are in the undo history: **Ctrl+Z** also takes back a material change (the files in `materials/`).
    - CLI: `tyrant mod materials <mod> <skin> --show | --set file.json | --clear | --ramp png | --mask png | --fur-mask png | --fur-infant-mask png | --preset <species> --kind iridescence|fur` (`--preset` starts the material from a vanilla species, as **Start from a species…** does; `--species <id>` for a species; `--species <id> --vanilla-skin <name>` for a vanilla skin; `--material <name>` picks the material).
  - **Species materials** (side list, **+ Add**): pick a species; its page is the same **Material** card, worn by every skin of the species under the skin's own settings. **Remove** deletes them. When two mods set the same species, the later one in the load order wins, and the Mods tab lists the clash (it is logged in the game too).
  - **Vanilla skin materials** (side list, **+ Add**): pick a species, then one of the game's skins. Its page is the same **Material** card; it restyles that game skin in place, for every animal wearing it. Mod skins based on that vanilla skin are not affected. **Remove** deletes them. A **From a map channel** mask on a vanilla skin uses the game's own texture at runtime. Files go to `materials/vanilla-skins/`, see `docs/mod-format.md`.
  - **A skin's files** list only the slots the species' shader uses (for example no fur for Stegosaurus, no pattern for
    Carcharodontosaurus), plus any the skin already has.
  - **Colours** also shows one animal in 3D above the six 2D animals: **Male / Female / Infant** switch its textures, **↻**
    shows another animal (the same one as the first 2D animal). It needs a data dump and an asset index (Workspace tab).
  - **Default texture compression** (mod details): Match the game or BC7 for all the mod's textures, with the estimated memory in game. Each texture slot and replacement has its own choice (Mod default / Match the game / BC7) and shows the format and size it gets. CLI: `tyrant mod set-compression <mod> auto|bc7|default [--texture <file>]`.
  - **Remove skin…** asks first and can delete its files; its number in the game stays reserved.
  - **Models** lists the species whose model the mod replaces; **+ Add** adds one here (pick the species, **Browse…** your `.glb`, **Add**). A model's page shows it in 3D with **LOD 0 / 1 / 2** (as the game draws it near and far), its vertices against the game's, its problems, and **Replace…**, **Rebuild LODs** and **Remove**. When you export your `.glb` again after adding it, the page says so and offers **Re-import**. A skin's **Model** row shows whether it wears its own model, the species replacement or the game's, with **Replace model…** and **Use species model**.
  - **Sounds** lists the mod's sound replacements with who hears them; **+ Add** picks a species (or **All sounds**) and **Replace…** on a sound adds it to this mod. A sound's page sets who hears it (**Only one species**, **Only one skin** or **Everyone**, then **Apply**), its files (**Add files…**, ✕ to drop one), **Volume** (0–2) and **Baby pitch** (how much higher young animals sound; 0 = like adults), **How often** (**Like the game**: your sound plays when the game's own does, keeping its silent turns; **My own chance**: a 0–100% slider), and **Remove**. CLI: `tyrant mod set-sound <mod> <event> --chance 0.3` or `--like-game`.
  - The **Mod** menu has Install, Check, Restore cutouts, Open folder and Remove from game.
  - If the mod's `mod.json` is changed elsewhere (another program, `tyrant mod …`), the tab reloads it when you come back.
- **Mods tab:**
  - **Check** lists problems: errors stop an install, warnings don't.
  - **Install to game** copies the mod into the game. The first time, it also installs MelonLoader and Tyrant's framework, after asking.
  - **On** switches a mod for the next game start. **Remove from game** takes it out again.
  - A mod installed before Tyrant cooked textures says **Reinstall to cook textures** (a game-only mod says *textures not cooked*); install it again to cook it.
  - *Changed since install* means you edited the workspace copy; install again to update the game.
  - When the framework in the game is older than this Tyrant, the Mods tab says so with an **Update Tyrant in game** button.
  - **Add mod from zip…** adds a shared mod to the workspace (it asks before replacing a mod with the same id).
  - **Bring in looks:** the game's in-game editor saves iridescence and fur changes into the installed copy of a mod (a mod made in the game, **Vanilla Looks**, is a mod that exists only there). **Install**, **Remove from game**, **Delete** (with removal from the game) and **Uninstall from game** would throw those away, so they first ask **Bring in looks made in the game?**. Each mod is listed with its changed material folders, each ticked **Bring in** (untick one to let it go). Where the game's copy and the project's both changed, choose **Game's** (the default) or **Project's**. A mod that exists only in the game gets **Copy to workspace first**. `UserData/Tyrant/Looks` (the looks of single animals, one file per save: `Looks/<save>.json`) and `UserData/Tyrant/Masks` (the images they may use) are not touched by any of this, not even by Uninstall. CLI: `tyrant mod looks <mod> [--bring-in] [--folder <folder>]` lists or brings them in; `install`, `remove`, `delete --yes`, `dump uninstall` and `game uninstall` stop with `GAME_LOOKS_PENDING` unless you add `--bring-in-looks` or `--discard-game-looks`.
- **Share a mod:** in the mod editor, **Mod → Export for sharing…** saves a zip players unzip into the game folder; its
  README says what it needs. CLI: `tyrant mod export <id>`, `tyrant mod import <zip>`.
- **Add a skin:**
  1. On the Species tab, click **Add a skin…** for a species.
  2. Fill in the form:
     - pick a mod (or **New mod…**) and name the skin;
     - choose the vanilla skin to start from and which sexes;
     - optionally tick **Also normal, extra, pattern and fur maps** (the babies' maps come along; the baby colour map always does).
  3. Tyrant copies that skin's textures into `mods\<id>\skins\<skin>\`. Edit them, then **Check** and **Install to game**.
  4. In the game's Nursery the new skin appears next to the vanilla ones, and the row scrolls when there are many.
  - This needs a data dump (Workspace → **Run data dump**).
- **Clean up skin numbers…** lists skins whose mods are gone; forget the ones you won't reinstall.
- **Restore cutouts** appears under a Check result when a colour PNG lost the see-through feathers or hair of its vanilla texture (an editor saved it without alpha). It copies the vanilla transparency back; install again afterwards.
- **In the game's Nursery,** a grid button next to **Design** (on every species with a skin to pick) opens **All skins**: every skin with a search box (skin or mod name). Click a skin to wear it; close with the X or Esc.
- **The in-game editor** has a small **T** button in two places. Click a placed animal: the button at the bottom of its info panel, beside **Sell** (and **Move**), opens the editor on that animal, on **This animal**. In the Nursery, the button right of the animation list (**Fidget**) opens it on the preview animal, on **Whole skin**, and the window follows the Nursery's skin, sex and species. In the Nursery, **This animal** is **Next created**: **Save** arms the look for the next animal you incubate from the preview (same species, skin and sex); **Incubate** binds it to that animal (kept with the save while it incubates or waits), and it becomes the animal's own look when you place it. It needs a park that was saved once. See [the in-game editor](mod-format.md#the-in-game-editor).
- **Update Tyrant in game** (Workspace tab) appears after a Tyrant update; installing a mod also updates the framework.
- **Start the game.** `MelonLoader\Latest.log` lists the mods Tyrant loaded, each texture it replaced and each sound the first time it plays.
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

## Custom species: the starting skin

A new species' form has a **Starting skin** section: **Copy a skin** (any base-game species and skin; another
species' textures need that species' model) or **My own PNGs** (male diffuse, optional female). The skin it makes
is listed in the side list with a *Starting skin* tag and opens a normal skin page (PNGs, colours, model, sounds,
portraits with **Render from 3D…**). The species page's Skins section then shows **Starting skin: <name>** with
**Open**. Species without one show the older controls and **Make an editable starting skin**.
**Make default** beside another skin in that list makes it the starting skin; the old one stays as an ordinary skin.

**Paleopedia photo**, under the species page's Prefab and look, sets the picture at the top of the species'
Paleopedia page and in the Nursery's species panel (a PNG; the game's are 512×256). **Use template photo** goes back
to the template's.

**Delete species…** at the bottom of the species page removes the species with its skins, models, sounds and
prefab settings, after a dialog that lists them and any other workspace mods that use the species. Its files go to
the Recycle Bin unless you untick that. Saved parks with the species can no longer load its animals.
