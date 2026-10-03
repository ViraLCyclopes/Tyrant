# PK Mod Studio

Modding toolset for **Prehistoric Kingdom** (Unity 2022.3, Mono). Work in progress — see
`docs/superpowers/specs/` for the design.

Principles: mods never replace or patch game files on disk; nothing from the game is redistributed —
all extraction happens locally from your own install.

## Build

Requirements: Windows, .NET 10 SDK.

```powershell
dotnet build PKModStudio.slnx
dotnet test PKModStudio.slnx
```

Integration tests against your real install (optional):

```powershell
$env:PK_GAME_DIR = "E:\SteamLibrary\steamapps\common\Prehistoric Kingdom"
dotnet test tests/PK.Core.Tests --filter "Category=Integration"
```

## CLI

```powershell
dotnet run --project cli/PK.Cli -- install detect                 # find the game via Steam
dotnet run --project cli/PK.Cli -- workspace init D:\pk-workspace # create a workspace (outside the game folder)
dotnet run --project cli/PK.Cli -- workspace status -w D:\pk-workspace
dotnet run --project cli/PK.Cli -- decompile -w D:\pk-workspace   # game code -> D:\pk-workspace\source
```

Assets (Addressables bundles):

```powershell
dotnet run --project cli/PK.Cli -- assets index -w D:\pk-workspace                     # ~20 s, writes cache\asset-index.json
dotnet run --project cli/PK.Cli -- assets list -w D:\pk-workspace --type Texture2D --filter stego
dotnet run --project cli/PK.Cli -- assets dump <guid|path|bundle#pathId> -w D:\pk-workspace --type Texture2D
dotnet run --project cli/PK.Cli -- assets export-textures -w D:\pk-workspace --filter stegosaurus
```

The GUIDs and container paths shown by `assets list` are what replacement mods target. DLC bundles are
indexed like any other once Steam has downloaded them (Steam > Properties > DLC).

Models and species packs:

```powershell
dotnet run --project cli/PK.Cli -- species list -w D:\pk-workspace
dotnet run --project cli/PK.Cli -- species pack "Stegosaurus Stenops" -w D:\pk-workspace   # models + textures + targets.json
dotnet run --project cli/PK.Cli -- assets export-model <prefab guid|path|bundle#pathId> -w D:\pk-workspace
```

Models are `.glb` files with the full skeleton, skin weights and the growth blend shapes (`Adolescent`,
`Infant`) — import them in Blender with File > Import > glTF 2.0. `targets.json` lists the prefab, mesh and
texture IDs plus the bone hierarchy that replacement models must keep. Each model's rest pose is the
prefab's saved pose (as Unity shows it), not the bind pose; Blender's glTF importer option "Guess Original
Bind Pose" (on by default) recovers the bind pose.

Verify output (optional, needs Node and Blender 5.2):

```powershell
npm install --prefix tools/verify
node tools/verify/validate-gltf.js (Get-ChildItem D:\pk-workspace\assets\species\stegosaurusstenops\models\*.glb).FullName
& "C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe" -b --factory-startup -P tools/verify/blender_inspect.py -- <model.glb> <render.png>
```

Every command accepts `--game <folder>` (where relevant) to skip Steam detection.
Exit codes: 0 ok, 1 usage error, 2 error (code printed, e.g. `GAME_NOT_FOUND`), 3 partial success.
Open `source\Assembly-CSharp\*.csproj` in Visual Studio or Rider to browse the decompiled game code.
