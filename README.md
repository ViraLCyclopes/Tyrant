<p align="center"><img src="docs/images/tyrant-logo.png" alt="Tyrant" width="160"></p>

# Tyrant: Prehistoric Kingdom Modding Toolkit

Tyrant is a free modding toolkit for **Prehistoric Kingdom**. Browse and export the game's textures, models and data;
make texture and skin mods (new skins appear in the Nursery); replace an animal's model with your own Blender edit;
replace game sounds, for everyone or for one species; and share mods as zips. Mods never replace or patch game files: Tyrant adds them at runtime and **Uninstall from
game** puts the game back to vanilla. Nothing from the game is redistributed; everything is read from your own install.

## Download

Get the latest **Setup** from [GitHub Releases](https://github.com/ViraLCyclopes/Tyrant/releases) and run it (no admin
rights needed). Tyrant updates itself from GitHub. Windows may warn that the publisher is unknown (Tyrant is not code-signed): click **More info → Run
anyway**.

## Start in three steps

1. **Workspace tab:** pick the game folder (found through Steam on its own) and an empty folder for your workspace, then
   **Refresh all** (asset index, data dump, game code).
2. **Workspace tab → Install Tyrant in game:** adds MelonLoader and Tyrant's framework (reversible).
3. **Make a mod:** on the Assets tab **Replace in a mod…** a texture, on the Species tab **Add a skin…**, or **Replace model
   in a mod…** on an animal's prefab, or **Sounds… → Replace…** on the Species tab; then **Install to game** and start the game.

To play a mod someone shared: **Mods tab → Add mod from zip…**, then **Install to game**. Without Tyrant: install
MelonLoader 0.7.3, unzip `Tyrant-Framework-<version>.zip` (from the releases page) and the mod's zip into the game folder.

## Blender

Tyrant works with **Blender 5.0 or newer** through its own Blender add-on.

1. **Install the add-on:** Workspace tab → **Blender** card → **Install add-on** (Tyrant finds Blender from Steam or
   Program Files; **Choose blender.exe…** if it doesn't). By hand: drag `Tyrant-Blender-Addon-<version>.zip` (from the
   releases page, or `sidecar\blender\tyrant_blender.zip` in Tyrant's folder) onto Blender, or Edit → Preferences →
   Get Extensions → Install from Disk.
2. **Open in Blender** on a species (Species tab, pick a game skin), a skin page or a model page of the mod editor. The
   model opens looking like the game: plain bones, merged vertices with UV seams marked, the game's materials (skin
   colours, pattern, eyes) and a **Growth** slider (Tyrant panel: press N → Tyrant) that shows the animal as a baby.
3. Sculpt or edit with Growth at 1 and the **Basis** shape key selected: the growth keys follow. Don't use Voxel Remesh
   (it deletes the shape keys).
4. **Send to Tyrant** (same panel): only the Tyrant armature and the meshes with an Armature modifier on it go — a ported
   mesh counts once you add that modifier; reference meshes, physics boxes and other rigs stay out. The first send asks
   which mod and which model (species or a skin); Tyrant's report (errors, warnings, sizes) shows in the panel.
   The panel lists what Send takes and why the rest stays, and stops with the fix in Blender words when something is
   missing (no Armature modifier, weights on another skeleton, a missing growth key).
5. **Textures:** paint or swap a texture in Blender and Send takes it too — only the ones you changed, unsaved painting
   included. On a skin it becomes the skin's map for the sex shown; on a species model it replaces the game texture.
6. **Ported models:** select your mesh and press **Use game material** in the Tyrant panel: your Base Color, Normal and
   Roughness become the game's material (the baby uses the same images; pattern and fur masks start blank). The game gives
   an animal one texture set, so bake several materials onto one first. Weights and growth keys are yours to make in
   Blender.
7. **FBX:** wherever a model goes in (Add model, a skin's model, `tyrant mod replace-model`) you can pick an `.fbx`; Tyrant
   has Blender convert it in the background. Assets export and species packs can write **FBX** or **glb + FBX** (the
   Model format box; `--format fbx|both` in the CLI). glb needs no Blender; FBX does.
8. **IK controls:** animals open with controls built from the game's own IK chains (Tyrant panel → IK controls): a foot or
   hand control with a knee or elbow pole per leg, a head control and an aim target. Each chain has an **IK** slider (key it
   to switch IK/FK in an animation) and the head an **Aim** slider. **Snap controls to pose** lines them up with a pose you
   made by rotating bones; **Bake this frame / Bake frame range** turns IK into plain bone keys; **Reset pose** (or
   Alt+G/R/S) returns to the exact rest pose that Send exports; models open in the game's prefab stance, which the
   legs and neck show through their controls (Growth stays). Send never takes the controls and keeps your pose. Blender's IK is close to the game's, not
   identical. Don't want them? Untick **IK controls** in Open in Blender's Options (`--no-ik` in the CLI), or **Remove IK
   controls** in the panel. `tyrant species ik <species>` (Species tab → **IK chains…**) lists a species' chains.

9. **Rig edits:** Tyrant panel → Rig edit → **Start rig edit**, then move, rotate or scale the game's bones in Pose Mode
   and press **Apply rig edit**: the mesh and the rest pose take only your edit, your stance stays, and the IK controls
   are rebuilt on the new legs (**Cancel** puts the pose back; **Clear rig edit** returns to the game's skeleton). Send
   takes the edit with the model: on the species model it changes every animal of that species (the game's skins too),
   on a skin only that skin. **Rig edit only (keep the game's mesh)** sends the edit alone (the game's mesh stretches
   with the bones). The panel warns about bones the game's animations or growth move. The mod editor's model and skin
   pages show the edit (**Clear rig edit**); `tyrant mod rig <mod> <species> [--skin <id>] [--clear]` and
   `tyrant species rig-info <species>` (Species tab → **IK and rig…**) do the same. When two mods change the same
   species, the later one in the load order supplies both its model and its rig edit (the Mods tab says so).

10. **Animations:** the game's animations, exactly as the game plays them (on a reshaped model too, rig edit included).
   In Blender: Tyrant panel → Animations → **Add animations…** (search, tick, Add): each becomes an Action; click one to play
   it. **In place** hides the walk's travel (keys untouched). While it plays the IK controls follow the feet and head;
   **Move to IK controls** hands those chains to the controls for editing (**Bake frame range** turns them back into bone
   keys). Growth keeps working while it plays. In the
   app: Species tab → **Animations…** to search them, **Open in Blender with these**, or **Export animations…** as **FBX**
   (default; one file per animation with the model, each animation an FBX take; your Blender makes the FBX) or glb. Open in
   Blender's Options can **Choose animations…** too, and species packs can take them (**With animations**). Better FBX users
   can open the FBX files with it; plain Blender, Maya, Max and Unity read them as well. CLI: `tyrant species animations
   <species>`, `tyrant species export-animations <species> <id…> | --all [--format fbx|glb|both] [--single-file]`,
   `tyrant blender open … --animations <id>`, `tyrant species pack --animations all`. Blender does not run the game's foot
   planting or look-at, so feet can sit slightly off where the game would plant them.

Each model opens as **its own scene in the file you have open** (e.g. *Tyrant · my-mod · Allosaurus Anax Red*); your other
scenes and objects are never touched, and saving the file is up to you. **Open in Blender** again switches back to that
scene. **Start fresh** (Options) keeps the old scene as *… (old)* and imports the model anew. To port a model, open the
Tyrant model in the file that holds your mesh, then bring the mesh into the Tyrant scene (Link to Scene, or Append).

## Updates

Tyrant checks GitHub for a new version once a day and offers **Update now**; **Help → Check for updates** checks any
time, and **Edit → Preferences** turns the daily check off.

More: the [app guide](docs/app-guide.md) and the [mod format](docs/mod-format.md).

## For developers

### Build

Requirements: Windows, .NET 10 SDK.

```powershell
dotnet build Tyrant.slnx
dotnet test Tyrant.slnx
```

Integration tests against your real install (optional):

```powershell
$env:TYRANT_GAME_DIR = "<your Steam library>\steamapps\common\Prehistoric Kingdom"
dotnet test tests/Tyrant.Core.Tests --filter "Category=Integration"
```

### CLI

```powershell
dotnet run --project cli/Tyrant.Cli -- install detect                 # find the game via Steam
dotnet run --project cli/Tyrant.Cli -- workspace init D:\tyrant-workspace # create a workspace (outside the game folder)
dotnet run --project cli/Tyrant.Cli -- workspace status -w D:\tyrant-workspace
dotnet run --project cli/Tyrant.Cli -- decompile -w D:\tyrant-workspace   # game code -> D:\tyrant-workspace\source
```

Assets (Addressables bundles):

```powershell
dotnet run --project cli/Tyrant.Cli -- assets index -w D:\tyrant-workspace                     # ~20 s, writes cache\asset-index.json
dotnet run --project cli/Tyrant.Cli -- assets list -w D:\tyrant-workspace --type Texture2D --filter stego
dotnet run --project cli/Tyrant.Cli -- assets dump <guid|path|bundle#pathId> -w D:\tyrant-workspace --type Texture2D
dotnet run --project cli/Tyrant.Cli -- assets export-textures -w D:\tyrant-workspace --filter stegosaurus
```

The GUIDs and container paths shown by `assets list` are what replacement mods target. DLC bundles are
indexed like any other once Steam has downloaded them (Steam > Properties > DLC).

Models and species packs:

```powershell
dotnet run --project cli/Tyrant.Cli -- species list -w D:\tyrant-workspace
dotnet run --project cli/Tyrant.Cli -- species pack "Stegosaurus Stenops" -w D:\tyrant-workspace   # models + textures + targets.json
dotnet run --project cli/Tyrant.Cli -- species textures "Allosaurus Anax" -w D:\tyrant-workspace    # its skins' textures, to replace in a mod
dotnet run --project cli/Tyrant.Cli -- assets export-model <prefab guid|path|bundle#pathId> -w D:\tyrant-workspace
```

Models are `.glb` files with the full skeleton, skin weights and the growth blend shapes (`Adolescent`,
`Infant`) — import them in Blender with File > Import > glTF 2.0. `targets.json` lists the prefab, mesh and
texture IDs plus the bone hierarchy that replacement models must keep. Each model's rest pose is the
prefab's saved pose (as Unity shows it), not the bind pose; Blender's glTF importer option "Guess Original
Bind Pose" (on by default) recovers the bind pose.

Verify output (optional, needs Node and Blender 5.2):

```powershell
npm install --prefix tools/verify
node tools/verify/validate-gltf.js (Get-ChildItem D:\tyrant-workspace\assets\species\stegosaurusstenops\models\*.glb).FullName
& "<path to Blender 5.2>\blender.exe" -b --factory-startup -P tools/verify/blender_inspect.py -- <model.glb> <render.png>
```

Game data (species stats, buildings, economy, localization):

```powershell
dotnet run --project cli/Tyrant.Cli -- dump install -w D:\tyrant-workspace     # installs MelonLoader 0.7.3 (checksum-verified) or reuses yours, + adds the dumper mod
dotnet run --project cli/Tyrant.Cli -- dump run -w D:\tyrant-workspace         # starts the game via Steam; it quits by itself after dumping
dotnet run --project cli/Tyrant.Cli -- data types -w D:\tyrant-workspace
dotnet run --project cli/Tyrant.Cli -- data show AnimalData Stegosaurus -w D:\tyrant-workspace
dotnet run --project cli/Tyrant.Cli -- data export AnimalData -w D:\tyrant-workspace   # -> exports\AnimalData.csv
dotnet run --project cli/Tyrant.Cli -- dump uninstall -w D:\tyrant-workspace   # removes exactly what install added
```

Mods (texture, model and sound replacements and skins; format in [docs/mod-format.md](docs/mod-format.md)):

```powershell
dotnet run --project cli/Tyrant.Cli -- mod new red-spot-carcharo --name "Red-spot Carcharodontosaurus" -w D:\tyrant-workspace
dotnet run --project cli/Tyrant.Cli -- mod replace red-spot-carcharo T_carcharodontosaurus_alt1_male_D [your.png] -w D:\tyrant-workspace
dotnet run --project cli/Tyrant.Cli -- mod add-skin red-spot-carcharo Carcharodontosaurus --name "Red spot" --base "Alt 1" -w D:\tyrant-workspace   # a new skin from a vanilla template (needs a data dump)
dotnet run --project cli/Tyrant.Cli -- sounds list --species Carcharodontosaurus -w D:\tyrant-workspace   # its sounds by moment (needs a data dump); --search click for any game sound
dotnet run --project cli/Tyrant.Cli -- mod replace-sound red-spot-carcharo "event:/…/TheroLarge_VoxSocialCall" call1.ogg call2.ogg --species Carcharodontosaurus -w D:\tyrant-workspace   # also: set-sound, remove-sound
dotnet run --project cli/Tyrant.Cli -- mod check red-spot-carcharo -w D:\tyrant-workspace
dotnet run --project cli/Tyrant.Cli -- mod restore-cutouts red-spot-carcharo -w D:\tyrant-workspace   # put back see-through feathers/hair an editor flattened
dotnet run --project cli/Tyrant.Cli -- mod install red-spot-carcharo -w D:\tyrant-workspace   # adds MelonLoader + Tyrant's framework if needed
dotnet run --project cli/Tyrant.Cli -- mod disable red-spot-carcharo -w D:\tyrant-workspace   # also: enable, remove
dotnet run --project cli/Tyrant.Cli -- mod export red-spot-carcharo -w D:\tyrant-workspace   # a zip players unzip into the game folder
dotnet run --project cli/Tyrant.Cli -- mod import red-spot-carcharo-1.0.0.zip -w D:\tyrant-workspace   # add a shared mod (--replace to overwrite)
dotnet run --project cli/Tyrant.Cli -- game status -w D:\tyrant-workspace   # framework version in the game and in this Tyrant
dotnet run --project cli/Tyrant.Cli -- game package-framework -w D:\tyrant-workspace   # the framework as a zip for players without Tyrant
dotnet run --project cli/Tyrant.Cli -- update check   # this version and the latest on GitHub
dotnet run --project cli/Tyrant.Cli -- mod list -w D:\tyrant-workspace
dotnet run --project cli/Tyrant.Cli -- mod clean-skins -w D:\tyrant-workspace   # list skin numbers left by removed mods; --forget <mod/skin> frees one
```

`dump install` and `mod install` are the only commands that add files to the game folder (MelonLoader, Tyrant's
dumper and framework mods, and installed mods under `UserData\Tyrant`); no game file is ever replaced, and the dumper
stays idle during normal play.
Tyrant uses MelonLoader, the loader the Prehistoric Kingdom modding community already uses; if you have
MelonLoader installed, only the mod is added (and only the mod is removed on uninstall).

Every command accepts `--game <folder>` (where relevant) to skip Steam detection.
Exit codes: 0 ok, 1 usage error, 2 error (code printed, e.g. `GAME_NOT_FOUND`), 3 partial success.
Open `source\Assembly-CSharp\*.csproj` in Visual Studio or Rider to browse the decompiled game code.

### The Tyrant app

The desktop app (Tauri 2 + SvelteKit) lives in `studio/` and runs `tyrant rpc` in the background.

Prerequisites: .NET 10 SDK, Node 24, Rust (stable, MSVC), Visual Studio Build Tools with the C++ workload, WebView2
(part of Windows 11).

    dotnet build Tyrant.slnx          # builds tyrant.exe, which the app uses in development
    cd studio
    npm install
    npm run tauri dev                 # run the app
    npm test                          # UI tests (vitest)
    npm run check                     # type and accessibility checks
    npm run gen:types                 # after changing RPC DTOs in core/Tyrant.Rpc
    npm run build:app                 # Setup program, copied to release/ (or double-click "Build Setup.bat")

See [docs/app-guide.md](docs/app-guide.md) for how to use it and [docs/app-smoke-test.md](docs/app-smoke-test.md)
for the release checklist.

## Licence

Tyrant is free software under the [GNU General Public License v3.0](LICENSE): you may use, study, share and change it;
if you share a changed version, share its source under the same licence. See [NOTICE](NOTICE) for the third-party
files it includes. Prehistoric Kingdom and its content belong to its developer and publisher; Tyrant is a fan-made tool
and is not affiliated with them. Using Tyrant, its name and logo, and sharing what you make with it: see the
[terms of use and trademark policy](TERMS.md).
