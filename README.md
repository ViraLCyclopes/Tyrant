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
$env:TYRANT_GAME_DIR = "E:\SteamLibrary\steamapps\common\Prehistoric Kingdom"
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
& "C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe" -b --factory-startup -P tools/verify/blender_inspect.py -- <model.glb> <render.png>
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

`dump install` is the only command that adds files to the game folder; the dumper mod stays idle during normal play.
Tyrant uses MelonLoader, the loader the Prehistoric Kingdom modding community already uses; if you have
MelonLoader installed, only the mod is added (and only the mod is removed on uninstall).

Every command accepts `--game <folder>` (where relevant) to skip Steam detection.
Exit codes: 0 ok, 1 usage error, 2 error (code printed, e.g. `GAME_NOT_FOUND`), 3 partial success.
Open `source\Assembly-CSharp\*.csproj` in Visual Studio or Rider to browse the decompiled game code.
