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
- **Making the model in Blender:** start from Tyrant's export of the prefab. Keep the armature and its bone names (new bones are not supported yet), keep the two growth shape keys (the game drives them from baby to adult), and keep the material names. Sculpt or edit (Sculpt Mode or Edit Mode) with the Basis selected in Shape Keys: Blender carries the change into the growth keys, so do not make it on them again (it would count twice). Do not use Voxel Remesh: it deletes the shape keys. A growth key that moves the mesh far more than the game's is a warning. The game's materials and textures stay; change the look with texture replacements or skin textures.
- **Levels of detail:** name extra meshes `…_LOD1` / `…_LOD2` to use your own; otherwise Tyrant simplifies your mesh to the game's own ratios.
- **Size:** more than 4× the game's vertices is a warning (many animals can lower the frame rate); over 65,535 vertices the mesh uses 32-bit indices.
- `file` must lie inside the mod; a path that leads outside it is an error, and Tyrant builds nothing for it.
- Check and Install rebuild the converted files when the `.glb` in the mod changed. Tyrant also remembers the file you added the model from; when you export it again, Check warns and the model's page offers **Re-import** (or run `tyrant mod replace-model` again).

## Rig edits (changing an animal's skeleton)

```json
"models": [
  { "target": "Allosaurus Anax", "rig": { "Jaw": { "move": [0, -0.05, 0.02] }, "Calve.L": { "scale": [1.1, 1.1, 1.1] } } }
]
```

and on a skin: `"rig": { … }` next to its `"model"`. Needs Tyrant framework **0.2.0** or newer.

| Field | Meaning |
|---|---|
| `move` | Metres, added to the bone's place (Unity space, in its parent's space). |
| `rotate` | A rotation (quaternion x, y, z, w) in the parent's space. |
| `scale` | Per axis, above 0. |

Each part is optional (left out = no change). Every frame the game composes the edit on the animation, like a JWE NODE
bone between the bone and its parent: place = move + rotate × (scale × place), rotation = rotate × rotation,
scale = scale × scale.

- **Made in Blender:** Tyrant panel → Rig edit → **Start rig edit**, pose the bones, **Apply rig edit**, then **Send to
  Tyrant**. Tyrant writes the `rig` and builds the model's bind poses for the edited skeleton.
- **Species or skin:** a species entry's rig edit applies to every animal of that species, the game's own skins
  included. A skin's rig edit (or its own model) replaces the species' for animals wearing it.
- **With or without a model:** with a model made for it, the mesh fits the edited bones. A `models` entry with a `rig`
  and no `file` keeps the game's mesh, which stretches with the moved bones.
- **Several mods on one species:** a species' model and rig edit go together. The later mod in the load order supplies
  both; the Mods tab names the mod it overrides.
- **Bones the game moves itself:** `tyrant species rig-info <species>` (Species tab → **IK and rig…**) lists the bones
  the animations move (an edit changes that motion) and the bones growth positions or scales (Check says whether edits
  on those work yet).

## Sounds (replacing game sounds)

Needs Tyrant Framework 0.1.0 or newer (Tyrant writes `requires.tyrant` for you when it exports the mod).

```json
"sounds": [
  { "event": "event:/AnimalFamily Master/Dinosaurs/Shared_TheropodLarge/_TheroLarge_Comp/Vox/TheroLarge_VoxSocialCall",
    "species": "Carcharodontosaurus",
    "files": ["sounds/carch-call-1a2b3c4d.ogg", "sounds/carch-call-2-5e6f7a8b.ogg"],
    "volume": 0.9 },
  { "event": "event:/User Interface/Buttons/UI_Click", "files": ["sounds/click-9a8b7c6d.wav"] }
]
```

| Field | Meaning |
|---|---|
| `event` | The game sound (an FMOD event path). Copy it from a species' **Sounds…** or **All sounds…** on the Assets tab, or from `tyrant sounds list`. |
| `species` | Only this species hears the new sound (the exact species id, e.g. `Carcharodontosaurus`). |
| `skin` | Only animals wearing this skin hear it: `<mod id>/<skin id>` for a Tyrant skin, `<species>/<skin name>` for a game skin (`Carcharodontosaurus/Alt 1`). Use `species` or `skin`, not both. |
| `files` | One or more audio files in the mod: WAV, OGG, MP3 or FLAC (Tyrant checks the contents, not the name). With several, one is picked at random each time, never the same one twice in a row. |
| `volume` | 0–2; 1 (the default) plays the file as it is. |
| `agePitch` | 0–1; how much higher babies play the sound, following the animal's age (1, the default: a newborn plays half again as high; 0: like adults). Interface sounds and music ignore it. |
| `chance` | 0–1, optional. Left out, the sound follows the game (below). Set, each time the game starts the sound it plays with this chance instead (0.3 = about one time in three). For one-off sounds only; looping sounds always follow the game. |

- **Which sound plays:** the animal's skin's replacement, else its species', else the one for everyone, else the game's
  own. Within one level, the later mod in the load order wins. Many animals share sounds (every large theropod uses the
  same calls): a replacement for everyone changes all of them, while a `species` one changes only that animal. When a
  shared call plays for a Carcharodontosaurus with its own call and for an Acrocanthosaurus at the same moment, each
  hears its own. Sounds the game plays in menus (the Nursery and Paleopedia calls) or without an animal can only be
  replaced for everyone; Check warns about a `species` or `skin` entry for one.
- **How it plays:** the game's sound is silenced and the file plays in its place, at the animal and in the game's own
  mixer, so the Sounds and Music sliders apply. A looping sound (breathing) loops the file until the game stops it; a
  one-shot plays the file to its end.
- **It follows the game:** the game's own sounds have chances built in (a growl is often a silent turn, about a third of
  the time for the large theropods) and can wait before playing. Tyrant keeps the game's sound running, silenced, and plays
  your file at the moment it really plays a sound, so those chances and that timing stay. An animal too far away to be heard
  stays silent too. With `chance`, your own chance replaces the game's.
- **When a file is missing or not audio:** Check and Install say so; in the game the original sound plays and the log
  says which file could not be opened.
- **Big files:** the game loads each file into memory; over 20 MB is a warning. OGG keeps files small.
- Tyrant copies your files into `sounds/` under names with a short content hash, so two files called `roar.wav` never
  clash.

## Sharing a mod

- **Export for sharing** (mod editor → Mod menu, or `tyrant mod export <id>`) runs Check first and refuses a mod with
  errors. It writes `<id>-<version>.zip`:
  ```
  UserData/Tyrant/Mods/<id>/mod.json
  UserData/Tyrant/Mods/<id>/<every file mod.json uses>
  README.txt
  ```
  Only mod.json and the files it names go in (with a model's `.lodN.tmesh` and `.model.json`); older model builds and
  anything else in the folder stay out. Players unzip it into the game folder. The README says it needs MelonLoader 0.7.3
  and Tyrant Framework (the version of the Tyrant that exported it) or newer, and how to install it.
- **Add mod from zip** (Mods tab, or `tyrant mod import <zip>`) accepts an exported zip, a zip of a mod folder, or a zip
  with mod.json at its root, and adds the mod to the workspace under mod.json's id, whatever its folder in the zip is
  called (a renamed folder, GitHub's `<repo>-main`). A damaged file, an unsafe path (`..`), no mod, two mods or an
  invalid id is refused. The shared mod.json names the framework version it needs (`requires.tyrant`), so an older
  framework skips it with a message; model reports in the zip carry no paths from the modder's PC. A mod with the same id is replaced only when you confirm
  (`--replace`), and only after the new one has unpacked.
- **The framework zip** (Workspace → Save framework zip…, or `tyrant game package-framework`) is
  `Tyrant-Framework-<version>.zip`: `Mods/Tyrant.Framework.dll`, `UserLibs/Tyrant.Framework*.dll` and `README.txt`, for
  players without Tyrant. Mods not listed in `mods.json` load after the listed ones, by id.

## In the game

- **Where installed mods live:** `<game>/UserData/Tyrant/Mods/<id>/`.
- **On/off and load order:** `<game>/UserData/Tyrant/mods.json`. When two mods replace the same texture, model or sound, the later one wins.
- **What the framework logs** in `<game>/MelonLoader/Latest.log`:
  - each mod it loaded;
  - each one it skipped, and why;
  - the first time each replacement is applied.
- **Removing:** **Uninstall from game** on Tyrant's Workspace tab removes MelonLoader, Tyrant's mods and every installed mod, which restores vanilla.
