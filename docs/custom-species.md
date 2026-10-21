# Custom species and animal databases (experimental)

Each mod's animal definitions live in a real **SQLite database named `animals.db`**.
They are not stored in `mod.json`. The file can be edited with SQL, a SQLite editor
or Tyrant's Species database page. It travels with the mod when installed or exported.

> **The Unity path is parked.** Tyrant no longer downloads tools to copy the template into Unity. Tune a species'
> prefab with **Prefab settings** (any field of its components) and give it its own look with **Open in Blender**.
> A species that already has a Unity bundle keeps loading it.
> Change how big a species is with **Sizes** (see [mod-format.md](mod-format.md#sizes-making-an-animal-bigger-or-smaller)).

The app no longer shows **Create / show Unity project** or **Attach Unity prefab bundle** while the Unity path is parked,
so those steps are not available. A species with an existing Unity bundle keeps loading it. The
[Unity authoring guide](../unity/SpeciesAuthoring/README.md) is kept for reference; this pipeline is experimental.
Tyrant restores PK's script assembly identities when loading a Unity bundle.

Recovered shader previews carry `Tyrant/Recovered/` names. When spawning the
authored animal, Tyrant rebuilds marked materials from the loaded vanilla prefab's
native materials and transfers authored property values. This retains PK's shader
keyword and render-pass state. Material names have a recovery marker too, so an
error shader does not hide their template identity. Unity previews are approximate.
The framework retains one native template addressable user per cached authored
prefab until shutdown. The native shader belongs to that dependency graph;
cloning a material alone does not keep its asset bundle loaded after previews
are disposed or replaced.
The animal inherits the template's animation and gameplay data and starts with **one** skin: a copy of the template skin you choose.

The runtime species implementation is experimental: spawning, AI and save/reload
still need an in-game test before this is ready for normal parks. Removing the mod
prevents the game from loading its animals from a save.

## In Tyrant

Open a mod and select **Species database**. Choose **New species…**, select a regular
animal template and fill in the id, display name, genus and epithet. **Create species**
creates `animals.db` and its first animal. Select that animal to edit its stats.
Creation copies the template's purchase cost and serialized animal fields into the database,
so they are visible and editable directly in a SQLite editor.
Blank fields inherit the template. **Reset stats to template** clears the overrides;
press **Save species** to apply the reset. Database edits currently clear the mod
editor's undo history, because its undo snapshots cover `mod.json` rather than SQLite.

The template supplies the skeleton, animations, growth and behavior. The new species
has its own stable identity and is unlocked when its template is (in career, research
the template to unlock it). In the Nursery it sorts by its scientific name, so a
Giganotosaurus based on Carcharodontosaurus sits with the G's. Vivarium
templates are not supported. Templates and ids are fixed in the form after creation.

## Outside Tyrant: SQL

Press **Show database file** or open:

```
<workspace>/mods/<mod-id>/animals.db
```

Use any SQLite-compatible editor. Commit your changes, then return to the mod tab
or window. Tyrant reloads committed changes and detects stale forms before saving.
Export and installation use SQLite snapshots, including committed edits in a WAL.
The running game uses the installed copy; install the mod again and restart the
game to apply workspace edits.

The database has eight tables:

| Table | Columns | Purpose |
|---|---|---|
| `animal_species` | `id`, `template`, `name`, `genus`, `epithet`, `cost` | Identity, template, display/scientific names, optional purchase cost |
| `animal_stats` | `species_id`, `stat`, `value` | Numeric overrides; primary key is `(species_id, stat)` |
| `animal_fields` | `species_id`, `path`, `kind`, `value`, `template_value`, `editable` | Full serialized fields and linked animal configurations |
| `animal_localization` | `species_id`, `slot`, `term` | Localization keys, with text in separate language files |
| `animal_assets` | `species_id`, `slot`, `file` | Picture PNG paths: `icon` (the Nursery picture) and `paleopedia` (the Paleopedia picture); see [Nursery and Paleopedia pictures](#nursery-and-paleopedia-pictures) |
| `animal_prefabs` | `species_id`, `bundle`, `asset` | Optional Unity AssetBundle and lowercase prefab asset path |
| `animal_starting_skin` | `species_id`, `skin`, `name`, `swatch` | Starting skin: the template skin number, optional name and swatch PNG (see [Skins](#skins)) |
| `animal_portraits` | `species_id`, `slot`, `file` | Starting skin portraits: slot (`adultMale` … `infantLeucistic`) to PNG path (see [Skins](#skins)) |

`PRAGMA user_version` is `6`. Existing v1–v5 databases upgrade on their next save (version 6 lets `animal_assets` hold the `paleopedia` slot). Species ids are unique; a stat references its species
id. Enable foreign keys when editing. If you create the database manually, use the
schema in [animal-database-schema.sql](animal-database-schema.sql).

For example, change an existing animal:

```sql
PRAGMA foreign_keys = ON;
BEGIN;

UPDATE animal_species
SET name = 'My custom animal', cost = 25000
WHERE id = 'example-animal';

INSERT OR REPLACE INTO animal_stats (species_id, stat, value)
VALUES ('example-animal', 'adultMassKg', 4500);

INSERT OR REPLACE INTO animal_stats (species_id, stat, value)
VALUES ('example-animal', 'maxAgeYears', 45);

COMMIT;
```

Add a distinct species using a template id from your data dump:

```sql
INSERT INTO animal_species (id, template, name, genus, epithet, cost)
VALUES ('example-animal', 'Allosaurus Anax', 'Example animal', 'Example', 'animal', 25000);
```

Delete a stat row to inherit that stat. Set `cost = NULL` to inherit the template's
purchase cost. Local ids use 3–64 lowercase letters, digits and hyphens, with no
hyphen at either end. Keep ids and the mod id stable: the save identity is
`tyrant.<mod-id>.<species-id>`. Display names and stats can change independently.

Additional tables and columns may be added by an external editor. Tyrant preserves
them when saving its known fields, but does not interpret them as gameplay settings.
Unknown `animal_stats.stat` names are errors, so a typo cannot silently look like
a working override. Invalid data is reported when opening or checking the mod.

## Supported stats

| Stat | Units |
|---|---|
| `adultMassKg`, `babyMassKg` | kg |
| `matureAgeYears`, `maxAgeYears` | years |
| `temperatureMinC`, `temperatureMaxC` | Celsius |
| `foliageMin`, `foliageMax` | fraction, 0–1 |
| `waterMin`, `waterMax` | fraction, 0–1 |
| `bedRequirement` | fraction, 0–1 |
| `vocalizationRangeM`, `footstepRangeM` | meters |

Minimum values must not exceed maximum values, including inherited endpoints.
The **Full animal record** section exposes the remaining serialized fields, including
diet, clade, popularity, dimensions, fence requirements, navigation, growth curves,
and the brain's adult/infant needs, behaviors, social rules, mating, reproduction,
and combat. Search by the game's field path. Numbers are stored as plain invariant
text, booleans as `true`/`false`, and enums as the game's enum names (comma-separated
for flags). Basic stats above and cost retain their existing tables and are not
duplicated in `animal_fields`.

```sql
UPDATE animal_fields SET value = '800'
WHERE species_id = 'mirabilis' AND path = '/brain/needs/adult/native/hitPoints';
UPDATE animal_fields SET value = 'Carnivore, Piscivore'
WHERE species_id = 'mirabilis' AND path = '/diet';
```

Leave `template_value` unchanged: it records the creation snapshot, and Tyrant applies
fields whose `value` differs. Changed graph branches are cloned, with brain parent
references rebound to the new species. Template assets are not edited.
Array elements and curve keys can be edited; array lengths, nulls, asset references,
identity links and localization asset internals are read-only. New array elements and
reference swaps are not supported by this editor. Asset expansion follows up to three
links (brain, module config, profile), stopping cycles and world/build databases.
This exposes serialized species settings, not computed properties, executable behaviors,
or every global game parameter. Editing dimensions does not physically reshape a mesh.

## Localization

PK uses Unity localization assets backed by CSV sheets. Tyrant stores only keys in
`animal_localization`, for `name`, `description`, `characterNotes`, and `socialInfo`.
Each field lives in its own UTF-8 text file. For example, the description key
`mirabilis/description` points to `localization/en-US/mirabilis/description.txt`.
That file contains only the description:

```text
Write this species' description here.

Additional paragraphs and <b>markup</b> are supported.
```

The name is separately stored in `localization/en-US/mirabilis/name.txt`.
Character notes and social info each get their own file, too. Other languages use
the same relative paths under `fr`, `es`, `pt`, `de`, `ru`, and `zh-CN` folders.
There are no JSON wrappers or key headers inside these files.

The database's description row points to that key. Creation writes an English name
and a placeholder description, plus empty files for every other language, ready to
translate. Empty or missing translations fall back to English. Empty or
missing notes retain the template's notes. At runtime Tyrant creates separate PK
localization assets and follows the current game language; original CSVs are unchanged.
The editor provides key inputs, a language selector, and separate buttons to reveal
each text file. Legacy combined language files remain readable for compatibility,
but new species write one plain text file per field.

## Nursery and Paleopedia pictures

A species has two pictures of its own, each set on the species page or with the CLI. Without one, the species shows
its template's.

**Nursery picture** is the game's `heroRender`: the picture on the species' Nursery card and at the top right of the
Nursery's species panel (also the education sign's species drop-down). The game's are 512×256 PNGs. On the species page
choose **Nursery picture → Choose PNG…**, or run
`tyrant mod set-species-photo <MOD> <SPECIES-ID> (--png <FILE> | --clear)` (also named
`set-species-nursery-picture`). Tyrant copies the PNG to `icons/<species-id>.png` and records it in `animal_assets`
with `slot = 'icon'`. It does not change the Paleopedia page.

**Paleopedia picture** is the big picture on the species' own Paleopedia page. The game does not take it from
`heroRender` but from the animal's info sign (`infoSign`, an `AnimalInfoSignCanvas` prefab whose first child's image is the
picture), which a new species shares with its template. With a Paleopedia picture, Tyrant gives the species its own copy
of that sign and puts the picture on the copy, so the template's page keeps its own. The game's are 2048×1152 PNGs
(16:9); use a 16:9 picture, for example 1280×720 or 2048×1152, or it is stretched. On the species page choose
**Paleopedia picture → Choose PNG…**, or run
`tyrant mod set-species-paleopedia-picture <MOD> <SPECIES-ID> (--png <FILE> | --clear)`. Tyrant copies the PNG to
`icons/<species-id>-paleopedia.png` and records it in `animal_assets` with `slot = 'paleopedia'`. The game uses the same
picture for the species' row in the Paleopedia's animal list and in a formation's species list, as the background of
its notifications (an egg hatched, an animal died, the species unlocked), for the most popular animal in the animals
overview, in objective pictures and as the preview in an education sign's info panel. The painted sign standing
in the park is not changed: the game draws it from a texture array of the base-game species' pictures, which
Tyrant does not extend.

**Use template picture** (or `--clear`) removes either override. Transparent PNGs are supported. SQL users may supply
another relative PNG path inside the mod. Check reports a picture PNG that is missing or unreadable. Skin swatches (a
species' starting skin swatch is set under [Skins](#skins)) are separate. Language files and picture PNGs are included
in mod exports and installs.

### What the Paleopedia page shows

A new species is listed in the Paleopedia's animal list like any animal. The Nursery panel's **Open Paleopedia** button
calls the game's `NurseryMenuV2.OpenPaleopedia`, which hands the page the species selected in the Nursery. The page is
expected to show the species' genus and species name, its description, character notes and social info (see
[Localization](#localization)), its Paleopedia picture and its stats. A field the species does not change is its
template's, so a species made from Allosaurus Anax with no other edits shows Allosaurus Anax's base rating (the
Nursery's Base Rating is the preview animal's own total, so its size and genetics change it), length, height, weight,
popularity, region, era and clade, its call and its narrated introduction. This has not been confirmed in game yet: one
report showed the template's page instead, and its cause is not known.

To find out, Tyrant watches the page. It records every time the game sets the Paleopedia's animal page (which animal, and
the game methods that asked for it). One frame after **Open Paleopedia** on a new species, it reads the page's header.
Each of these lines is written to the MelonLoader log once per species per game session:

- `Open Paleopedia shows Ultimasaurus anax (tyrant.…)'s own page (set with … by …).`: the page was right.
- `Open Paleopedia showed Allosaurus anax (set with <animal> by <game methods>, <n>s earlier) instead of Ultimasaurus anax
  (tyrant.…); switched it.`: the header showed another animal. Tyrant set the page to the species again and it now shows
  it. The `set with` part names the last animal the page was set to and what set it; `which threw …` there, or
  `Open Paleopedia threw …` at the end, means the game's method failed part way.
- The same line ending `switching it left … on the page.` or `switching it threw …` means it could not be fixed.

If you see a `showed … instead of …` line, please report it with the lines around it.

At runtime Tyrant reads `animals.db`, clones the template's animal data, applies
the overrides and registers the result in PK's in-memory animal database before
AI tables initialize. PK's game database files are not modified.

## Skins

### The starting skin

Every new species starts with its own **starting skin**: an ordinary skin in your mod, editable like any
other (its PNGs, colours, own model, sounds, rig edit, swatch and portraits on its skin page). The new-species
form's **Starting skin** section picks where its PNGs come from:

- **Copy a skin**: any base-game species' skin (default: the template and its first skin). Every map is copied
  into the mod (diffuse, normal, extra, pattern, fur and the babies' maps, for each sex the skin has). A sex the
  skin has no textures for wears the template's game skin. Copying from another species (say Carcharodontosaurus
  PNGs on an Allosaurus-based species) only lines up on that species' model: give the skin (or the species) that
  model; Check reminds you.
- **My own PNGs**: a male diffuse PNG and optionally a female one (default: the male one). The other maps come
  from the template skin so the skin is complete; replace them on the skin page.

In mod.json it is a `skins[]` entry with `"starting": true` and `"base": "0"` (see the mod format). In game it is
the species' skin 0, so saved animals keep it. Other skins on the species with base `0` wear its textures and
colours under their own (not its model, rig or sounds: put a model meant for every skin on the species).

Delete the starting skin and the species starts with its template's game skin again. Species made before
starting skins existed keep the template's game skin until you click **Make an editable starting skin** on the
species page.

To make another of the species' skins in the same mod its starting skin, click **Make default** beside it in the
species page's Skins list (or `tyrant mod set-starting-skin <MOD> <SKIN>`). If the species already has a starting
skin, what the skin took from it (PNGs for maps it has none of, colours, portraits, swatch) is copied into the skin
first, so it looks the same; the old starting skin stays as an ordinary skin based on `0`. From then on, skins based
on `0` take what they lack from the new starting skin, and animals placed with skin 0 in saved parks wear it.

### Species without a starting skin

Such a species starts with a copy of the template skin chosen as **Starting skin** (default: the template's
first). Change it, rename it, or give it its own swatch PNG (the round picture in the skin picker) in the species
page's **Skins** section. These live in `animals.db`'s `animal_starting_skin` table (`skin` = the template skin's
number, `name`, `swatch`); a species with a starting skin keeps only `skin` there (the game object it is copied from).

Such a starting skin can also have its own in-game portraits (the pictures the game shows for the animal by sex, growth stage
and colour variant; the 12 slots are `adultMale`, `adultMaleAlbino`, `adultMaleMelanistic`, `adultMaleLeucistic`, the same for
`adultFemale`, and `infant`, `infantAlbino`, `infantMelanistic`, `infantLeucistic`). Set them in the app under the species page
(Skins → Portraits), or with `tyrant mod set-species-portrait <MOD> <SPECIES-ID> --slot <SLOT> (--png <FILE> | --clear)`.
They are stored in the `animal_portraits` table (`species_id`, `slot`, `file`). Portraits are 2:1 (the game's are 128x64); a slot
left out shows the template skin's portrait.

Add more skins with **Add skin** on that section, or from the mod's side list: they are ordinary
`skins[]` entries whose `species` is the species' id (`tyrant.<mod-id>.<species-id>`) and whose `base` is
`"0"` (the starting skin). Added skins are numbered from 1.

Another mod can add skins to your species the same way: its Add skin lists "Other mods' species" (mods in
the workspace and mods installed in the game). In game, if your mod is missing or off, only those skins are
skipped; the rest of the other mod still loads. Tyrant's mod details and Mods list say so.

## Deleting a species

**Delete species…** at the bottom of the species page (or `tyrant mod remove-species <MOD> <SPECIES-ID>`) removes
the species from `animals.db` together with this mod's skins for it (its starting skin too), its models and rig
edits, the sounds for it or its skins, and its prefab settings. The dialog lists what goes and which other mods in
the workspace still have skins, models or sounds for it (the game skips those). Its files go to the Recycle Bin
too: PNGs, models, sounds, language files, icon, swatch, portraits, prefab bundle and Blender projects, except
files something else in the mod still uses. Untick that (or pass `--keep-files`) to leave them. It cannot be undone
with Ctrl+Z, and saved parks with the species can no longer load its animals, as when its mod is removed.

## CLI

```powershell
tyrant mod add-species my-animal-pack example-animal "Allosaurus Anax" --name "Example animal" --genus Example --epithet animal --cost 25000 -w <workspace>
tyrant mod show my-animal-pack -w <workspace>
tyrant mod check my-animal-pack -w <workspace>
```

```powershell
tyrant mod add-species my-animal-pack example-animal "Allosaurus Anax" --name "Example animal" --genus Example --epithet animal --start-from "Carcharodontosaurus/Alt 1" --start-name "Carch look" -w <workspace>
tyrant mod add-species my-animal-pack own-look "Allosaurus Anax" --name "Own look" --genus Example --epithet own --start-png male_D.png --start-png-female female_D.png -w <workspace>
tyrant mod add-starting-skin my-animal-pack older-animal --start-from "Allosaurus Anax/1" -w <workspace>
tyrant mod add-species my-animal-pack example-animal "Allosaurus Anax" --name "Example animal" --genus Example --epithet animal --starting-skin 1 -w <workspace>
tyrant mod set-species-skin my-animal-pack example-animal --starting-skin 0 --name "Mirabilis" --swatch swatch.png -w <workspace>
tyrant mod add-skin other-pack tyrant.my-animal-pack.example-animal --name "Blue" -w <workspace>
tyrant mod set-starting-skin my-animal-pack blue -w <workspace>
tyrant mod set-species-photo my-animal-pack example-animal --png nursery.png -w <workspace>
tyrant mod set-species-paleopedia-picture my-animal-pack example-animal --png paleopedia.png -w <workspace>
tyrant mod remove-species my-animal-pack example-animal -w <workspace>
```

Use the app or SQL for other stats. The CLI's `mod add-skin` and `mod replace-model`
can target the namespaced species identity and resolve its template prefab and maps.
