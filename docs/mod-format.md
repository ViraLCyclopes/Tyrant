# Tyrant mod format (format 1)

A Tyrant mod is a folder. Tyrant makes it for you (Mods tab, or `tyrant mod new`), but it is plain files you can edit.

```
red-spot-carcharo/
  mod.json
  textures/
    T_carcharodontosaurus_alt1_male_D.png
```

## mod.json

```json
{
  "format": 1,
  "id": "red-spot-carcharo",
  "name": "Red-spot Carcharodontosaurus",
  "version": "1.0.0",
  "author": "you",
  "description": "optional",
  "requires": { "tyrant": ">=0.1" },
  "dependencies": [],
  "replace": [
    {
      "texture": "T_carcharodontosaurus_alt1_male_D",
      "key": "Assets/Art/Animals/Dinosaurs/Carcharodontosaurus/Textures/T_carcharodontosaurus_alt1_male_D.png",
      "guid": "e3583acd2b3b5b14c875f42d110d97ce",
      "file": "textures/T_carcharodontosaurus_alt1_male_D.png"
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `format` | Always `1` for now. |
| `id` | 3–64 lowercase letters, digits or `-`. It is also the folder name. |
| `name`, `version`, `author`, `description` | Shown in Tyrant and in the game log. |
| `requires.tyrant` | The oldest Tyrant framework that runs the mod. |
| `dependencies` | Ids of mods that must be installed and on. |
| `assembly` | (code mods) a DLL in the mod folder with a `Tyrant.Framework.TyrantMod` subclass. |
| `replace` | Game textures to swap at runtime. `texture` is the game texture's name (copy it from the Assets tab); `file` is your PNG, inside the mod folder. `key` and `guid` are filled in by Tyrant. |

## Textures

- **Slots:** any animal skin texture can be replaced: diffuse (`_D`), normal (`_N`), extra, pattern and fur, for adults and infants.
- **Size:** keep the original size (Tyrant's check warns otherwise). Sizes divisible by 4 are compressed like the game's own.
- **Normal maps:** edit the blue-purple PNG Tyrant exports. The framework converts it to the game's packed form.
- **Genetics colours:** the game tints skins with genetics colours on top of your texture.
- **See-through parts (cutouts):** feathered and furry species (Velociraptor, Gallimimus, Megaloceros, …) cut feathers and hair out with the diffuse's transparency. Keep the alpha channel when you save. **Check** warns when a colour PNG has lost it, and **Restore cutouts** (Mods tab, or `tyrant mod restore-cutouts <id>`) copies the vanilla transparency back without touching your colours.

## Skins (adding new skins)

A mod can add new skins next to the vanilla ones; nothing vanilla changes. Make one with **Add a skin…** (Species tab) or
`tyrant mod add-skin`, which copies a vanilla skin's textures into the mod as a template.

```json
"skins": [
  {
    "id": "red-spot",
    "species": "Carcharodontosaurus",
    "name": "Red spot",
    "base": "Alt 1",
    "thumbnail": "skins/red-spot/thumbnail.png",
    "male":   { "diffuse": "skins/red-spot/male_D.png", "normal": "skins/red-spot/male_N.png" },
    "female": { "diffuse": "skins/red-spot/female_D.png" }
  }
]
```

| Field | Meaning |
|---|---|
| `id` | Unique within the mod (lowercase letters, digits, `-`). |
| `species` | The game's species id (Tyrant fills it in). |
| `name` | Shown in the Nursery. |
| `base` | The vanilla skin to start from (name or number). Everything the skin doesn't provide comes from it. |
| `thumbnail` | Optional swatch; otherwise cut from the diffuse. |
| `male` / `female` | Slots to PNGs: `diffuse`, `normal`, `extra`, `pattern`, `fur`, `infantDiffuse`, `infantNormal`, `infantExtra`, `infantPattern`, `infantFur`. Leave a sex out for a single-sex skin (that sex looks like the base). |

**Numbers and removal:**
- Each added skin gets a permanent number in `<game>/UserData/Tyrant/skin-slots.json`, so saved animals keep their skin whatever mods you add, remove or reorder.
- Added skins are numbered from 15 (or after the vanilla skins, if a species has more), never right after the vanilla ones, so a game update that adds vanilla skins does not collide with them. The numbers in between are hidden stand-ins.
- When a skin's mod is removed, its animals look like their species' first skin until the mod comes back.
- **Clean up skin numbers…** (Mods tab) frees numbers of mods you won't use again.

## In the game

- **Where installed mods live:** `<game>/UserData/Tyrant/Mods/<id>/`.
- **On/off and load order:** `<game>/UserData/Tyrant/mods.json`. When two mods replace the same texture, the later one wins.
- **What the framework logs** in `<game>/MelonLoader/Latest.log`:
  - each mod it loaded;
  - each one it skipped, and why;
  - the first time each replacement is applied.
- **Removing:** **Uninstall from game** on Tyrant's Home tab removes MelonLoader, Tyrant's mods and every installed mod, which restores vanilla.
