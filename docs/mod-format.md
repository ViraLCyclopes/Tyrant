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
| `replace` | Game textures to swap at runtime. `texture` is the game texture's name (copy it from the Assets tab); `file` is your PNG, inside the mod folder. `key` and `guid` are filled in by Tyrant (built-in textures have neither; the name is enough). |

## Textures

- **Slots:** any animal skin texture can be replaced: diffuse (`_D`), normal (`_N`), extra, pattern and fur, for adults and infants.
- **Any object:** textures of fences, paths, buildings and scenery (the Assets tab's **Built-in** groups and the shared bundles) can be replaced too. The framework points each game material that uses the texture at yours when a park loads and as new objects appear.
- **Same name twice:** when several game textures share a name, all of them are replaced; **Check** warns and lists the files.
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

## Skin colours

A skin can set its own colours in a `colors` section. Every part is optional; anything left out comes from the base skin.
(The app's mod editor sets all of this for you: open the mod, pick a skin, **Colours**. `tyrant mod colors <mod> <skin> --set file.json` does the same from the command line.)

```json
"colors": {
  "tint":    { "hue": [-0.05, 0.05], "saturation": 0, "value": 0 },
  "pattern": { "a": ["#3060ff", "#2040c0"], "b": "#20c040", "strength": [0.6, 0.8], "softness": [0.1, 0.35],
               "secondary": "#ffcc00", "eye": "#ff2000" },
  "albino":  { "a": "#ffffff", "eye": "#ff4060" }
}
```

| Key | Meaning |
|---|---|
| `tint` | The random hue, saturation and value shift (each -1 to 1) of normal animals. `0` means exact colours. It only shows where the pattern map's red is above 0. |
| `pattern` | Pattern colours for **normal** animals (vanilla only colours mutations). `a`, `b`: colours where the pattern map's red is low or high. `secondary`: where its green is set. `eye`: the eyes. `strength` (0–1): how much the colour replaces the texture. `softness` (> 0): how soft the edge between `a` and `b` is. |
| `albino`, `melanistic`, `leucistic` | The same keys, used for that mutation, plus its own `tint`. |

- **Colours:** `#rrggbb`, or a list of up to 8 for a gradient; each animal gets a random point on it.
- **Ranges:** `[min, max]`, or one number for a fixed value.

**Painting the maps:**
- **Pattern red:** 0 keeps the texture, so leave mouth, claws and eyes black. Dark grey takes colour `a`, light grey takes colour `b`.
- **Pattern green:** marks the secondary-colour areas.
- **Extra red:** smoothness. Above about 90% (230) is treated as **eyes**, so keep skin below that. Check warns otherwise.
- **Extra green:** ambient occlusion.
- **Blue and alpha** of both maps are unused.
- **Diffuse alpha:** below 50% is cut away (feathers and hair).

## Models (replacing an animal's mesh)

```json
"models": [
  { "target": "Carcharodontosaurus", "key": "Assets/Prefabs/Animals/V2-MainPrefabs/Carcharodontosaurus.V2.prefab", "file": "models/carcharodontosaurus-1a2b3c4d.glb" }
]
```

and on a skin: `"model": "models/carcharodontosaurus-spiked-5e6f7a8b.glb"`.

| Field | Meaning |
|---|---|
| `target` | The species id the model replaces (objects such as fences come in a later update). |
| `key` | The prefab Tyrant matched (filled in by Tyrant). |
| `file` | Your Blender export, copied into the mod. Tyrant writes the converted files next to it: `….lod0.tmesh`, `….lod1.tmesh`, … (one per level of detail) and `….model.json` (what it found). |

- **Which model an animal wears:** its skin's `model`, else its species' entry in `models`, else the game's. When two mods replace the same species, the later one in the load order wins.
- **Making the model in Blender:** start from Tyrant's export of the prefab. Keep the armature and its bone names (new bones are not supported yet), keep the two growth shape keys (the game drives them from baby to adult), and keep the material names. Reshape the Basis in Edit Mode: Blender carries the change into the growth keys, so do not make it on them again (it would count twice). A growth key that moves the mesh far more than the game's is a warning. The game's materials and textures stay; change the look with texture replacements or skin textures.
- **Levels of detail:** name extra meshes `…_LOD1` / `…_LOD2` to use your own; otherwise Tyrant simplifies your mesh to the game's own ratios.
- **Size:** more than 4× the game's vertices is a warning (many animals can lower the frame rate); over 65,535 vertices the mesh uses 32-bit indices.
- `file` must lie inside the mod; a path that leads outside it is an error, and Tyrant builds nothing for it.
- Check and Install rebuild the converted files when the `.glb` in the mod changed. Tyrant also remembers the file you added the model from; when you export it again, Check warns and the model's page offers **Re-import** (or run `tyrant mod replace-model` again).

## In the game

- **Where installed mods live:** `<game>/UserData/Tyrant/Mods/<id>/`.
- **On/off and load order:** `<game>/UserData/Tyrant/mods.json`. When two mods replace the same texture, the later one wins.
- **What the framework logs** in `<game>/MelonLoader/Latest.log`:
  - each mod it loaded;
  - each one it skipped, and why;
  - the first time each replacement is applied.
- **Removing:** **Uninstall from game** on Tyrant's Workspace tab removes MelonLoader, Tyrant's mods and every installed mod, which restores vanilla.
