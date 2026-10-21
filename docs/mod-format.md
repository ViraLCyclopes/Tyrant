# Tyrant mod format (format 1)

A Tyrant mod is a folder. Tyrant makes it for you (Mods tab, or `tyrant mod new`), but it is plain files you can edit.

Experimental custom species use a separate SQLite `animals.db` alongside `mod.json`.
See [Custom species and animal databases](custom-species.md) for the SQL schema and stat editor.

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
    "portraits": { "adultMale": "skins/red-spot/portraits/adultMale.png" },
    "male":   { "diffuse": "skins/red-spot/male_D.png", "normal": "skins/red-spot/male_N.png" },
    "female": { "diffuse": "skins/red-spot/female_D.png" }
  }
]
```

| Field | Meaning |
|---|---|
| `id` | Unique within the mod (lowercase letters, digits, `-`). |
| `species` | The game's species id, or a custom species' `tyrant.<mod-id>.<species-id>` (from this mod or another). Tyrant fills it in. |
| `name` | Shown in the Nursery. |
| `base` | The vanilla skin to start from (name or number). Everything the skin doesn't provide comes from it. For a custom species, `0` (its starting skin). |
| `starting` | Optional, `true` on a custom species' **starting skin**: the species' skin 0 itself (no number of its own). Only on this mod's own species, one per species, with `base` `"0"` (the template skin the species is copied from). Other skins on the species with base `0` wear its textures and colours under their own. The game ignores a flag that breaks these rules (the skin loads as an added skin) and Check reports it. |
| `copiedFrom` | Optional, `{ "species": "Carcharodontosaurus", "skin": 1 }`: the game skin of another species a starting skin's PNGs were copied from. **Edit a copy** copies from it. |
| `group` | Optional. Groups the skin in Tyrant's mod editor (e.g. `"Jurassic Park"`); the game ignores it, and the order of `skins` too. |
| `thumbnail` | Optional swatch; otherwise cut from the diffuse. |
| `portraits` | Optional in-game portraits: slot (`adultMale`, `adultMaleAlbino`, … `infantLeucistic`, the 12 the game has) → PNG, 2:1 (the game's are 128×64). A slot left out shows the base skin's. |
| `male` / `female` | Slots to PNGs: `diffuse`, `normal`, `extra`, `pattern`, `fur`, `infantDiffuse`, `infantNormal`, `infantExtra`, `infantPattern`, `infantFur`. Leave a sex out for a single-sex skin (that sex looks like the base). |

**Numbers and removal:**
- Each added skin gets a permanent number in `<game>/UserData/Tyrant/skin-slots.json`, so saved animals keep their skin whatever mods you add, remove or reorder.
- Added skins are numbered from 15 (or after the vanilla skins, if a species has more), never right after the vanilla ones, so a game update that adds vanilla skins does not collide with them. The numbers in between are hidden stand-ins. For a custom species (`tyrant.<mod-id>.<species-id>`) numbering starts at its skin count, normally 1.
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
| `albino`, `melanistic`, `leucistic` | The same keys, used for that mutation, plus its own `tint`. Each key left out keeps the base skin's value, so a mutation you do not touch looks as it does on the base skin. |

- **Colours:** `#rrggbb`, or a list of up to 8 for a gradient; each animal gets a random point on it.
- **Ranges:** `[min, max]`, or one number for a fixed value.

**Painting the maps:**
- **Pattern red:** 0 keeps the texture, so leave mouth, claws and eyes black. Dark grey takes colour `a`, light grey takes colour `b`.
- **Pattern green:** marks the secondary-colour areas.
- **Extra red:** smoothness. Above about 90% (230) is treated as **eyes**, so keep skin below that. Check warns otherwise.
- **Extra green:** ambient occlusion.
- **Blue and alpha** of both maps are unused.
- **Diffuse alpha:** below 50% is cut away (feathers and hair).

## Skin materials

A skin, a vanilla skin or a whole species can change how the animal's material looks: a rainbow sheen (**iridescence**,
like a boa's scales), fur masks and some of the animal shader's settings. Nothing vanilla changes. In the app: open the
mod, pick a skin, **Material**; for a whole species, **Species materials** in the side list; for a game skin,
**Vanilla skin materials**. `tyrant mod materials` does the same from the command line (`--show`, `--set file.json`,
`--clear`, `--ramp png`, `--mask png`, `--fur-mask png`, `--fur-infant-mask png`; `--species <id>` for a species,
`--species <id> --vanilla-skin <name>` for a vanilla skin). The [in-game editor](#the-in-game-editor) writes the same files.

**Where they live.** A mod's materials are not in `mod.json`; each owner has a folder with a `materials.json` and its images:

| Folder | Owner |
|---|---|
| `materials/skins/<skinId>/` | One of the mod's skins |
| `materials/species/<species>/` | A whole species (a custom species too) |
| `materials/vanilla-skins/<species>-<skinName>/` | A skin of the game, restyled in place |

Folder names are made safe for files (characters a file name cannot hold become `_`); two owners that end up with the same
name get `-2`, `-3`. The owner comes from the file, not the folder name:

```json
{
  "format": 1,
  "species": "Titanoboa",
  "skin": "Alt 1",
  "materials": {
    "Titanoboa": {
      "iridescence": {
        "strength": { "normal": 0.35, "albino": 0.2 },
        "variation": 0.15,
        "ramp": { "thinFilm": { "thickness": 420 } },
        "mask": { "from": "extra", "channel": "g", "invert": false },
        "scale": -0.25, "power": -0.3, "bias": 1.3
      },
      "settings": { "_Smoothness": 0.55, "_RimIntensity": 0.2, "_FresnelColor": "#40c0ff", "_Droopage": 0.6, "_DroopDirection": [0, -1, 0] },
      "fur": { "mask": "materials/vanilla-skins/Titanoboa-Alt 1/fur-mask.png" }
    }
  }
}
```

- `format` is `1`. A skin's file has only `materials`; a species' file also has `species`; a vanilla skin's file has
  `species` and `skin` (the skin's name in the game, for example `Alt 1`).
- A `mod.json` that still has an inline `materials` (on a skin or in a `models` entry) is refused, and the message names
  the folder it moved to. Move the block into that folder's `materials.json` (the app's mod editor and `tyrant mod materials`
  write the folders for you).
- The key under `materials` is the material's name. For now only the animal's own material works, and its name is the
  species' material name (for example `Titanoboa`); any other name is an error. Extra materials (eyes, props) come later.

| Key | Meaning |
|---|---|
| `iridescence.strength` | A number 0–1 for every animal, or an object with `normal` (required) and optionally `albino`, `melanistic`, `leucistic`. A mutation left out uses `normal`. `0` turns iridescence off, also on skins that have it in vanilla. Leaving `iridescence` out keeps the game's settings: only some vanilla skins shimmer (Microraptor's, for example), as their own skin data says. |
| `iridescence.variation` | 0–1. How much strength differs between animals, as a fraction of the strength (0.15: each lands within ±15 %). Default `0.15` when `strength` is set. Each animal's offset comes from its ID, so it is stable across save and load and nothing is saved. An animal without an ID (the Nursery hologram) uses the set strength. |
| `iridescence.ramp` | The colours, along the viewing angle: a PNG in the mod (`"materials/skins/rainbow/iridescence-ramp.png"`), `{ "stops": ["#ff0000", …] }` (1 to 32 colours, evenly spaced) or `{ "thinFilm": { "thickness": 420, "index": 1.56 } }` (`thickness` 0–2000 nm, `index` 1–3, default 1.56). `"game"`: the game material's own ramp, also over a ramp a lower level (species, starting skin) sets. `{ "species": "Microraptor" }`: that vanilla species' ramp, by reference (see **Starting from a species** below). Left out: the lower level's, else the game's. |
| `iridescence.mask` | Where it shows: `"everywhere"`, a PNG in the mod, or `{ "from": "extra", "channel": "g", "invert": false }` (`from` is `extra`, `pattern` or `fur`; `channel` is `r`, `g`, `b` or `a`). `"game"`: the game's own mask, over a lower level's. `{ "species": "Microraptor" }`: that vanilla species' mask, by reference, at any level (a species file too). Left out: the lower level's, else the game's. |
| `iridescence.tiling`, `iridescence.offset` | `[x, y]`, beside `mask`: repeat and shift the mask. |
| `iridescence.scale`, `power`, `bias` | −10 to 10. Shape the strength across the viewing angle (edge falloff, falloff curve, brightness offset). |
| `fur.mask`, `fur.infantMask` | The fur mask of adults and of babies: a PNG in the mod (`"materials/skins/rainbow/fur-mask.png"`). `"game"`: the game's own per-animal mask, over a lower level's. Left out: the lower level's, else the game's (a baby mask left out uses the adult one). One file serves both sexes. PNG files or `"game"` only: no `everywhere`, no `from`. |
| `fur.grow` | `true` grows fur on skins the game gives none (Ouranosaurus, Allosaurus Anax, …): the game's fur shader variant, full fur amount, and, where no level sets a fur mask and the game has none, fur on the whole body (a `"game"` fur mask keeps the game's). `false` means this level does not force fur over a lower one that does; it never takes away fur the game has. Left out: the levels below decide. Works on skins, vanilla skins, species and per-animal looks. |
| `settings` | Animal shader values, below. |

**Growing fur.** Grown fur needs the game's tessellated fur (its Fur graphics setting); with tessellation off the game draws no grown fur
(Tyrant still sets the fur amount and masks, so the fur appears when tessellation is turned back on) and the MelonLoader log says so once. Set `_Length` too (the in-game editor
and the app start it at `0.5` when nothing gives the fur any length). Bodies never made for fur have no fur mask, droop or
wind tuned for them, so the look needs tuning in game: a fur mask PNG limits where it grows, and `_Length`, `_Droopage`,
`_DroopDirection` and the wind settings shape it.

**Starting from a species.** A look can start from a vanilla species: its iridescence (the species whose skins shimmer:
Microraptor, Velociraptor, Oviraptor, Edmontosaurus, …, listed from the game's data, not fixed) or its fur (species with a
fur map). Picking one (the app's **Start from a species…**, the in-game editor's **Preset** row, or
`tyrant mod materials … --preset <species> --kind iridescence|fur`) fills the block, and everything stays editable:

- **Iridescence:** `ramp` becomes `{ "species": "<id>" }`; `mask` too when the look is for that same species, and
  `"everywhere"` on any other species (a mask is painted for its own species' UV layout, so on another body it would land
  anywhere) or when the species' material has no mask. `scale`, `power` and `bias` are copied from that species' material,
  `strength` from its first shimmering skin (a mutation's own where it differs; not known: a strength already there stays),
  and iridescence is on.
- **Fur:** `fur.grow` is `true`; `_Length`, `_Droopage`, `_FurWindStrength`, `_FurWindSpeed` and `_FurWindScale` are copied
  from the species' material, `_DroopDirection` from its first furred skin's adult droop (kept within ±20). The fur mask stays.
- Numbers are copied as plain numbers, kept within their ranges. **The game's images are never copied into a file**: the
  datamining policy forbids sharing game files in mods, so a ramp or mask stays a reference to the species. In the game,
  Tyrant loads that species' material in the background and copies the texture into one of its own, in memory only; until it
  is ready (and if it cannot be read: one warning in the log) the animal shows the game's own ramp or mask. The app's preview
  reads it from your game files. **Check** reports a `species` that is not a vanilla species of the data dump, is spelt
  differently from the game's id, or whose material has no such texture.

**Vanilla skins.** A file in `materials/vanilla-skins/` restyles one of the game's own skins in place: every animal wearing it
changes, in every park, while the mod is on. The key is `"<species>/<skinName>"` (for example `Titanoboa/Alt 1`). A mod skin
that is based on that vanilla skin is **not** affected; it keeps its own settings. The app lists these under **Vanilla skin
materials**; the CLI takes `--species` with `--vanilla-skin`.

**Masks and ramps:**
- **`from` masks** (a channel of a skin's map) work on mod skins and on vanilla skins, not on species (a species has no map
  of its own). On a mod skin they use the mod's map for that slot (its own, or its starting skin's); for a slot the mod has
  no PNG for, the game's map (its base skin's) at runtime, the same as on a vanilla skin, which always points at the game's
  own texture. No image from the game is written anywhere, and the app's preview does the same.
- **Ramp PNGs** are never compressed, and slim installs keep them. **Mask PNGs** (iridescence and fur) are cooked like other data maps.
- Images imported in the app (**Import image**) or with `--ramp` / `--mask` / `--fur-mask` / `--fur-infant-mask` are copied into
  the owner's folder with fixed names: `iridescence-ramp.png`, `iridescence-mask.png`, `fur-mask.png` and `fur-infant-mask.png`.
- **Sharing** carries them: ramp PNGs always go in the zip; mask PNGs too, or inside `textures.tpack` when the export
  leaves the PNGs out, and **Add mod from zip** brings them back. Removing a skin with its files sends its images to the
  Recycle Bin.

**Settings** (`settings`; numbers outside the range are errors, and a key not in this table is an error):

| Key | Label in the app | Range |
|---|---|---|
| `_Smoothness` | Surface · Smoothness | 0–1 |
| `_Specularity` | Surface · Specularity | 0–1 |
| `_OcclusionStrength` | Surface · Ambient occlusion | 0–10 |
| `_AdultNormalIntensity` | Surface · Adult normal strength | −10–10 |
| `_InfantNormalIntensity` | Surface · Baby normal strength | −10–10 |
| `_DetailScale` | Detail · Detail tiling | 2–4 numbers, each −100–100 |
| `_MicroBumpStr` | Detail · Micro bump strength | 0–1 |
| `_MicroBumpMaskedScale` | Detail · Micro bump (masked) scale | 0–1 |
| `_RimIntensity` | Rim / fresnel · Rim light | 0–10 |
| `_FresnelColor` | Rim / fresnel · Rim colour | a colour, `#rrggbb` |
| `_FresnelPower` | Rim / fresnel · Rim falloff | 0–10 |
| `_EyeEmission` | Eyes · Eye glow | 0–1 |
| `_EyeSmoothness` | Eyes · Eye smoothness | 0–1 |
| `_EyeSpecularity` | Eyes · Eye specularity | 0–1 |
| `_Length` | Fur · Fur length | 0–4 |
| `_Droopage` | Fur · Fur droop | 0–2 |
| `_DroopDirection` | Fur · Droop direction | 3 numbers (x, y, z), each −20–20 (not a unit vector: vanilla skins go from about −10 to 8) |
| `_FurWindStrength` | Fur · Fur wind strength | 0–10 |
| `_FurWindSpeed` | Fur · Fur wind speed | 0–10 |
| `_FurWindScale` | Fur · Fur wind scale | 0–10 |
| `_Cutoff` | Cutout · Cutout threshold | 0–1 |

**Which setting wins**, per field: this animal (a look made in game, below) > the skin's (a mod skin or a vanilla skin) >
its base (starting) skin's > its species' > the game's. Between mods, the later mod in the load order wins a block, and the
Mods tab lists the clash (the framework logs it in the game too). Mod Check does not report it. The mod `vanilla-looks`
(shown as **Vanilla Looks**), which the game creates for edits of vanilla skins, always loads last.

**What it can and cannot do:**
- The shimmer's hue follows the **viewing angle** only, not the light, and runs across the body's curve rather than
  scale by scale. Real rainbow boas flash mostly in highlights; Tyrant approximates the look.
- The app's 3D preview approximates the game too. A `_Smoothness` override is previewed as a multiplier on the extra map's
  smoothness; check the real look in the game.
- Nothing is written into parks or saves. Remove the mod and every animal is vanilla again.

Materials need Tyrant Framework 0.2.0 or newer.

## The in-game editor

Tyrant Framework adds a small **Tyrant** button (a **T**) in two places:

- **In the park:** click an animal (park or vivarium). The button is at the bottom of its info panel, beside **Sell** (and **Move**), and opens the editor on
  that animal, on **This animal**.
- **In the Nursery:** the button is right of the animation list (**Fidget**) and opens the editor on the preview animal, on
  **Whole skin** (**Whole species** is there too). In its place, **This animal** is **Next created**: the look for the next
  animal you incubate from the preview, of that species, skin and sex. **Save** arms it ("Saved for the next Allosaurus you
  incubate."), in memory only: leaving the park before an incubation drops it. **Incubate** binds it to that very animal
  ("Bound to <name>, incubating."): the binding is kept in this save's looks file, under `"incubating"` and the animal's
  virtual id, so it survives saving and loading while the animal incubates or waits in the holder. When you place that
  animal, the look becomes its own (`"animals"`, like any **This animal** look); no other animal ever takes it. The binding
  remembers the species, skin and sex it was made for, and an animal of another kind placed under the same virtual id
  (the game can hand an id out again) never gets it: the binding is dropped instead. A binding whose animal is gone
  (placed, incubation cancelled, sold) is dropped when the park is saved, and one the loaded save does not have when the
  park is loaded (bound, then left without saving). A look that
  takes its mask from a map of one sex arms only for that sex; any other also goes to an incubation of the other sex when
  nothing was armed for it. **Next created** needs a park that was saved once (the status line says so), and **Export mask
  PNG** needs the placed animal. When you change the Nursery's skin, sex or species,
  the window follows the preview animal. Closing the Nursery is like the animal going away, except that unsaved
  changes stay in the window until you **Save** or **Reset** them.

The window edits the animal it was opened on until you close it or open it from another animal. Drag it by its title,
resize it from the corner (at least 300 × 360; it opens in the middle of the screen and keeps its size), and switch between **This animal**, **Whole skin**
and **Whole species**. The tabs **Iridescence** and **Fur** hold the settings above as value boxes you can type into; **Game's** (shimmer colours, mask, fur mask) saves `"game"`, so the game's own shows even over a whole skin's or species' choice. Each tab starts with a **Preset** row, `‹ None · Microraptor · … ›` and **Apply**: it starts the draft from that species (see **Starting from a species**), with no question asked over unsaved changes (**Reset** still goes back to the saved look); while the species' material still loads, the status line says *Loading …* and the preset applies once it is ready. **Shimmer colours** and **Mask** also offer **Species**, with `‹ name ›` to pick the species; **Export mask PNG** does not export a species' mask (it stays a reference). If a
game update moves the panels and a button cannot be placed, the MelonLoader log says so once, with what it found there.

- **Live preview:** every change shows at once on every animal it affects. It is a preview until **Save**; **Reset** goes
  back. Closing the window, opening it on another animal, or a new skin or species in the Nursery asks what to do with
  unsaved changes (keep or discard). Kept in the Nursery, the window stays on its look until you save or reset it.
- **Shimmer colours** (the ramp): **Thin film**, **Colours**, **Game's**, **PNG** or **Species**. **Colours** is a ramp of
  your own, saved as `{ "stops": [...] }` (the same file the app writes): one colour for a single tone like Microraptor's,
  two for blue to green, up to 8 in the window. It starts from the colours in effect (a lower layer's are copied into this look, all of them; a thin film's, at most 8),
  else Microraptor's `#39149c` → `#2e6be1`. A strip shows the gradient; click a swatch to pick a colour, **+** adds a copy
  after it, **-** removes it. The colour square sets its saturation (across) and brightness (up), the strip beside it its
  hue; drag either and the animals follow. Or type `#rrggbb` in the box. A look from the app with more than 8 colours
  keeps them all: the window edits the first 8.
- **Export mask PNG** writes the mask as a PNG to edit in an image editor. It is staged until **Save**, then written under the
  fixed file name (`iridescence-mask.png`, `fur-mask.png`, …) and becomes the mask. **Reload images** picks up your edits
  without restarting. The PNG pickers list the target's folder and `UserData/Tyrant/Masks`.

| What you edit | Save goes to |
|---|---|
| A mod skin | That mod's folder in the game (whoever made it) |
| A vanilla skin | The mod whose block for it currently applies; otherwise the `vanilla-looks` mod, created for it |
| A custom species | Its own mod |
| **This animal** | `UserData/Tyrant/Looks/<save>.json`, named after the save file and keyed by the animal's persistent ID (its images in `Looks/<save>/<id>/`); never the park save |

- Looks of single animals follow the **save file**, not the park's name: loading a save reads its own file, and every save
  (Save As and autosaves too) writes its own copy, with the animal's images copied into that save's folder. Deleting a save
  in the game deletes its looks. A new park needs its first save before **This animal** can be saved.
- A look on **This animal** stays with the animal when you move it. Looks of animals that have left the park are dropped
  (with their images) the next time the park is saved; only the file of the save being loaded or written is ever changed.
- Saves into a mod change the **game's copy**; a manual mod update replaces those edits. Before you install, remove, delete or
  uninstall, the app offers to bring them into the project (**Bring in looks**, see the app guide), or use
  `tyrant mod looks <mod> --bring-in`. Uninstalling Tyrant keeps `UserData/Tyrant/Looks` and `UserData/Tyrant/Masks` (looks point at images there); without Tyrant the park loads vanilla.
- Install records what it copied in `UserData/Tyrant/install-stamps.json`, which is how Tyrant tells what was changed in the game.

## Texture compression (`textures`)

Tyrant compresses ("cooks") every skin texture and texture replacement when it installs or exports the mod, into one
file, `textures.tpack`, next to mod.json. The game then loads each texture ready-made instead of decoding the PNG. The
PNGs stay the mod's source; never edit `textures.tpack` by hand (Tyrant rewrites it, and the game ignores an entry whose
PNG changed and uses the PNG instead).

Install also leaves the same `textures.tpack` in the workspace's mod folder, and **Cook textures** on the mod's details
page (`tyrant mod cook <id>`) writes it there without installing. The page says whether it is up to date. Export never
takes it from the folder: it cooks a fresh one into the zip. If the mod folder is in git, add `textures.tpack` to
`.gitignore`.

Install leaves the skin PNGs that are in `textures.tpack` out of the game folder (texture replacements keep theirs,
and so does a texture also used as a thumbnail, portrait or icon), and gives a skin without a thumbnail a `thumbnail.auto.png` cut from its diffuse. Export does
the same unless you tick **Include the original PNGs** (`tyrant mod export <id> --with-pngs`). Import, **Copy to
workspace** on the Mods tab (`tyrant mod copy-from-game <id>`) and **Extract textures…** (`tyrant tpack extract <file>
--out <folder>`) turn a pack back into PNGs; those are the cooked textures, slightly lower quality than the originals.
Mods without their PNGs need Tyrant Framework 0.2.0 or newer.

```json
"textures": {
  "compression": "auto",
  "overrides": { "skins/red-spot/male_D.png": "bc7" }
}
```

- `compression`: `auto` (default) matches the game — DXT1 for opaque colour and masks, DXT5 for textures with
  transparency and for normal maps. `bc7` is best quality at the size of DXT5 (twice DXT1). Tyrant compresses BC7 on the graphics card (a few seconds per texture; longer without one).
- `overrides`: one texture's own setting (file as written in `replace` or a skin slot).
- Thumbnails, portraits and species icons are not cooked.

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

## Prefab settings

A mod can change any serialized field of a species' prefab components (physics, IK, LODs and so on) without replacing
the prefab. Each target gets one file in the mod's `prefabs/` folder. `mod.json` does not list these files; Tyrant
finds them by folder.

```json
{
  "format": 1,
  "target": "Spinosaurus",
  "fields": [
    { "object": "", "component": "AnimalPhysics", "field": "inputParams.waterlineHeight", "value": 3.5 },
    { "object": "IK/LegFrontL", "component": "FABRIKComponentAnimal", "field": "core.influence", "value": 0.8 },
    { "object": "", "component": "LODGroup", "field": "lods[1].screenRelativeTransitionHeight", "value": 0.2 }
  ]
}
```

- `target`: a base-game species id (`Spinosaurus`) or a custom species identity (`tyrant.<mod-id>.<species-id>`).
  The file name is only a filesystem-safe form of the target; readers use `target` from the content.
- `object`: the GameObject's path from the prefab root, names joined by `/`. `""` is the root. With duplicate sibling
  names the path takes the first one.
- `component`: the type's simple name (the script class, or `LODGroup` / `Animator`). For two components of the same
  type, `#n` picks the n-th, counting from 0 (`FABRIKComponentAnimal#1`).
- `field`: serialized field names joined by `.`, with `[i]` for list or array elements (`chain[2].boneLength`).
- `value`: a JSON value of the field's type: a number, true/false, a string, an enum name (or its number),
  `{"x":..,"y":..,"z":..}` (plus `"w"`) for vectors and quaternions, `{"r":..,"g":..,"b":..,"a":..}` for colours.

If several mods set the same field on the same target, the later mod in load order wins, per field. Fields the other
mods set are kept. A file with no fields is deleted. Mod Check errors on two files with the same target or an unknown
target, and warns on a field the template prefab does not have or a value of the wrong type.

**Editable fields:**

- Every serialized field of a game MonoBehaviour on any object in the prefab, if its type is a number, bool, enum,
  string, Vector2/3/4, Quaternion or Color, or a struct, class or list of these. List lengths are read-only; only
  existing elements can be edited. Object references are read-only.
- `LODGroup`: `lods[i].screenRelativeTransitionHeight`.
- `Animator`: `applyRootMotion` and `cullingMode`.
- Not editable: `Transform` (use rig edits), renderers, meshes, materials, the Animator's controller and avatar, and
  Unity's internal `m_*` fields on other built-in components.

## Sizes (making an animal bigger or smaller)

A mod can change how big a species is, without replacing its model. Each target gets one file in the mod's `sizes/`
folder. `mod.json` does not list these files; Tyrant finds them by folder.

```json
{ "format": 1, "target": "Tyrannosaurus", "adult": 4, "baby": 0.10, "male": 1.0, "female": 1.1,
  "nursery": [0.5, 3], "breeding": [0.85, 1.15] }
```

- `target`: a base-game species id or a custom species identity (`tyrant.<mod-id>.<species-id>`), as for prefab settings.
  The file name is only a filesystem-safe form of the target; readers use `target` from the content.
- Every key except `target` is optional, and a left-out key means the game's value.
- `adult`: multiplies the real adult size (0.05 to 20).
- `baby`: the newborn's size as a share of the adult's (above 0, at most 1).
- `male`, `female`: multiply the size of each sex (0.1 to 20).
- The maximums reach Tyrant's 20×, the most a species' automatic size limit allows (see below).
- `nursery`: the range of the Nursery's Customize size slider for this species. The random size of uncustomised eggs
  stays as in the base game.
- `breeding`: the range of the random size of this species' bred young.
- `nursery` and `breeding` are each `[min, max]` with 0 < min <= max <= 20. In this game the base ranges are the same
  for every species: Nursery 0.925 to 1.075 and breeding 0.85 to 1.15 (a custom species has its template's, which are
  the same).
- There is no size limit to set: it is automatic (see below). A `limit` key from an older Tyrant is read but ignored;
  Check warns "The size limit is automatic now; this file's limit is ignored and is removed the next time you save.",
  and the next save of the file leaves it out.
- A key outside its bounds, or one Tyrant does not know, is an error.

If several mods set the same key on the same target, the later mod in load order wins, per key. Keys the other mods set
are kept. The species' size limit then follows the merged sizes.

The genetic size an animal gets is saved with it in the park. An animal made with a wider range (the Nursery slider or
breeding) keeps that size after the mod is removed: a T. rex made at 1.5 stays 1.5 times a vanilla one.

**A custom species' old record edits.** Before sizes existed, a mod's own species could change its growth curve
(`/scaleGrowthCurve/...`) and its skins' `maleSizeMultiplier` and `femaleSizeMultiplier` as record edits in `animals.db`.
Those edits still apply first, and the sizes multiply on top. The field editor no longer shows them, and Check warns
about them. **Remove old size edits** on the species' Size card, or `tyrant mod size <mod> <species> --remove-record-edits`,
takes them out of the record, so only the sizes change it.

**What follows the real size.** An adult's size relative to its species stays as in the base game, so these follow the
size you set: guest appeal and cost, sale value (a bigger species sells for more), mass and combat damage, height,
width and radius.

**What stays as in the base game for adults:** turning and banking, awareness, aggression range, enclosure needs, and
incubation time. The game times incubation by the growth curve, not by the real size, and Tyrant keeps the curve's adult
value, so only a hatchling's time changes, and by less than its size does.

**The size limit is automatic.** The game keeps every animal's size between 0.05 and 2 (it logs an error each time a size
passes that). Tyrant gives each species the limit its sizes need instead, worked out the same way by the framework in
game, by Check and by Blender. It counts the species' smallest and largest size: the youngest baby to the adult, the sex
multipliers of every skin and its genetic ranges (the random eggs', its `nursery` and its `breeding`, else the game's).

- The limit is never narrower than the game's 0.05 to 2. Past it, the max is the largest size rounded up to the next
  0.5, and the min is the smallest rounded down to the next 0.01.
- Each keeps 0.1% to spare. That is the margin the framework's scale-back keeps inside a limit, so a largest of 2.999
  gets 3.5 (the game would back anything past 2.997 off under a limit of 3).
- Tyrant's own bounds are 0.01 to 20: no species goes past them.

In game, the framework changes the game's one check (`AnimalNative.CurrentScale`) to read each animal's species' limit,
but only once a species needs more than 0.05 to 2. While every species fits the game's limit, nothing is changed. If a
game update changes that code, the framework leaves it alone and logs one warning; sizes then keep to the game's 0.05
to 2, and the framework scales the adult back until they fit. The log names each species' limit, for example
`Tyrannosaurus: size limit 0.05 to 13.5 (from its sizes).`

**The minimum.** The game's smallest size is 0.05×, and the automatic limit can lower it to 0.01×. `adult` takes 0.05
at least, but babies count: the smallest size is the youngest baby (its `baby` share of the adult) of the smallest sex
with the smallest genetic size. So a small adult with a small baby can go under 0.05×.

**Past Tyrant's bounds.** Tyrant's Check reports a species whose sizes would pass 20× or go under 0.01× as an error. It
names the species as the game shows it, and the animal its sizes are relative to (a custom species' template), for
example: "The biggest Tyranno Maximus can reach 26.3× a normal Tyrannosaurus (adult 2.63 × Nursery slider up to 10),
more than Tyrant's 20× limit; lower the adult size or the size ranges." The smallest case reads the same way: "The
smallest … can be …× a normal … (a hatchling: adult … × baby … × breeding down to …), less than Tyrant's 0.01× limit;
raise the adult size, the baby size or the size ranges." In game, the framework scales the adult back until the
sizes fit 20× (or 0.01×) and logs one warning.

**Past 2×.** A size past the game's 2 (within 20) is allowed, and Check warns about it for a species whose sizes or
ranges can pass 2: "{species} can grow past the game's normal 2× limit; the size class, camera framing, foot
placement and enclosure needs were tuned for normal sizes." The game was made for normal sizes, so a very big animal
may clip into things, get stuck or be framed badly.

**A wide Nursery slider.** For a size slider that goes far past the game's, set a wide Nursery range on the species:

```json
{ "format": 1, "target": "Tyrannosaurus", "nursery": [0.25, 5] }
```

The slider then covers 0.25 to 5 times the normal genetic size for that species, and its automatic limit lets the
biggest of them show (5 × a T. rex's own sex size, up to Tyrant's 20×).

**In Blender.** The Tyrant panel's **Growth** slider scales the armature object as the game scales the animal:
curve(m) × (1 + (s − 1) × f(m)), with the species' growth curve (rebuilt for the mod's `adult` and `baby`), s the shown
sex's multiplier with the mod's `adult` and `male` or `female` applied, and the game's maturity fade f(m). This is a view
only: the mesh and bones keep their size, and **Send to Tyrant** exports at the object's own scale. The **Size** box edits
the four size keys when the model goes to a species (a base-game species model or a mod's own species); Send to Tyrant
then writes the keys edited in Blender into that species' `sizes/` file (`tyrant blender send --sizes <file>`), keeping
the others as the mod has them, so a change made in the app meanwhile stays. The ranges are set in the app only, and
the size limit is automatic. For a skin it shows the species' sizes read-only, since sizes apply to the whole species. The view shows the size
you set, within Tyrant's own bounds (0.01 to 20) only. The project file carries the species' automatic limit. Since
that limit follows the sizes up to 20×, the Size box has a note only when the species (every skin's sexes, baby to
adult, its genetic ranges, counted as Check does) would pass 20× or go under 0.01×: "In game this is capped at 20×."
(or "… at 0.01×."). Send to Tyrant raises nothing: it writes the sizes, and the limit follows them.

## Rig edits (changing an animal's skeleton)

```json
"models": [
  { "target": "Allosaurus Anax", "rig": { "Jaw": { "move": [0, -0.05, 0.02] }, "Calve.L": { "scale": [1.1, 1.1, 1.1] } } }
]
```

and on a skin: `"rig": { … }` next to its `"model"`.

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
- **Neck and head (head look):** the game aims an animal's head from a "boom" measured per species on its original
  skeleton (from the base of the neck to the head). For a species entry's rig edit, Tyrant measures how the edit moves
  the neck and head and stores it as `"headRig": { "lengthScale": 1.2, "boomTurn": [x, y, z, w], "headTurn": [x, y, z, w] }`
  plus the `rigHash` it was measured for (written for you when the rig edit is saved; Install and Export measure it again
  if the rig edit changed since). The framework re-aims the species' boom with it, keeping the game's tuning. The boom
  is per species: a skin's neck or head edit cannot re-aim it, and skins with their own model or rig edit keep their own
  neck but get the species' re-aimed boom. Check warns about both; the fix is to put the edit on the species or make a
  custom species for it.

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
