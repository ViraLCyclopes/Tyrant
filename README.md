# Tyrant

Modding toolset for **Prehistoric Kingdom** (Unity 2022.3, Mono). Work in progress — see
`docs/superpowers/specs/` for the design.

Principles: mods never replace or patch game files on disk; nothing from the game is redistributed —
all extraction happens locally from your own install.

## Build

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

## CLI

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

Mods (texture replacements; format in [docs/mod-format.md](docs/mod-format.md)):

```powershell
dotnet run --project cli/Tyrant.Cli -- mod new red-spot-carcharo --name "Red-spot Carcharodontosaurus" -w D:\tyrant-workspace
dotnet run --project cli/Tyrant.Cli -- mod replace red-spot-carcharo T_carcharodontosaurus_alt1_male_D [your.png] -w D:\tyrant-workspace
dotnet run --project cli/Tyrant.Cli -- mod add-skin red-spot-carcharo Carcharodontosaurus --name "Red spot" --base "Alt 1" -w D:\tyrant-workspace   # a new skin from a vanilla template (needs a data dump)
dotnet run --project cli/Tyrant.Cli -- mod check red-spot-carcharo -w D:\tyrant-workspace
dotnet run --project cli/Tyrant.Cli -- mod restore-cutouts red-spot-carcharo -w D:\tyrant-workspace   # put back see-through feathers/hair an editor flattened
dotnet run --project cli/Tyrant.Cli -- mod install red-spot-carcharo -w D:\tyrant-workspace   # adds MelonLoader + Tyrant's framework if needed
dotnet run --project cli/Tyrant.Cli -- mod disable red-spot-carcharo -w D:\tyrant-workspace   # also: enable, remove
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

## The Tyrant app

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
    npm run build:app                 # installer: src-tauri/target/release/bundle/nsis/

See [docs/app-guide.md](docs/app-guide.md) for how to use it and [docs/app-smoke-test.md](docs/app-smoke-test.md)
for the release checklist.

## Licence

Tyrant is free software under the [GNU General Public License v3.0](LICENSE): you may use, study, share and change it;
if you share a changed version, share its source under the same licence. See [NOTICE](NOTICE) for the third-party
files it includes. Prehistoric Kingdom and its content belong to its developer and publisher; Tyrant is a fan-made tool
and is not affiliated with them.
