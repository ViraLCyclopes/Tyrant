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

Every command accepts `--game <folder>` (where relevant) to skip Steam detection.
Exit codes: 0 ok, 1 usage error, 2 error (code printed, e.g. `GAME_NOT_FOUND`), 3 partial success.
Open `source\Assembly-CSharp\*.csproj` in Visual Studio or Rider to browse the decompiled game code.
