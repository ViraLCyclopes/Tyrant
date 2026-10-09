# Custom species and animal databases (experimental)

Each mod's animal definitions live in a real **SQLite database named `animals.db`**.
They are not stored in `mod.json`. The file can be edited with SQL, a SQLite editor
or Tyrant's Species database page. It travels with the mod when installed or exported.

For an authored Unity prefab, use **Create / show Unity project** on an existing
species. This creates a project under `<workspace>/authoring/<mod-id>/Unity`, outside
the shared mod. **Import template animal into Unity** recovers the vanilla prefab
and its linked assets with a pinned local AssetRipper helper. It forks asset GUIDs,
binds scripts to the project's PK references and disables the prefab root. Existing
authoring files are preserved. Follow its README, build a bundle in Unity 2022.3.62f3, then enter
the export report's asset path and attach the bundle in Tyrant. See the
[Unity authoring guide](../unity/SpeciesAuthoring/README.md). This pipeline is
experimental. Unity compilation, PK component serialization and bundle build/load
checks pass for the recovered Spinosaurus prefab; it still needs an in-game test. Tyrant prepares
local authoring DLLs automatically and restores PK's script assembly identities
when attaching a Unity bundle.

Recovered shader previews carry `Tyrant/Recovered/` names. When spawning the
authored animal, Tyrant rebuilds marked materials from the loaded vanilla prefab's
native materials and transfers authored property values. This retains PK's shader
keyword and render-pass state. Material names have a recovery marker too, so an
error shader does not hide their template identity. Unity previews are approximate.
The framework retains one native template addressable user per cached authored
prefab until shutdown. The native shader belongs to that dependency graph;
cloning a material alone does not keep its asset bundle loaded after previews
are disposed or replaced.
The animal continues to inherit template animation, gameplay and skin data.

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
has its own stable identity and is always unlocked in the first version. Vivarium
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

The database has six tables:

| Table | Columns | Purpose |
|---|---|---|
| `animal_species` | `id`, `template`, `name`, `genus`, `epithet`, `cost` | Identity, template, display/scientific names, optional purchase cost |
| `animal_stats` | `species_id`, `stat`, `value` | Numeric overrides; primary key is `(species_id, stat)` |
| `animal_fields` | `species_id`, `path`, `kind`, `value`, `template_value`, `editable` | Full serialized fields and linked animal configurations |
| `animal_localization` | `species_id`, `slot`, `term` | Localization keys, with text in separate language files |
| `animal_assets` | `species_id`, `slot`, `file` | Custom icon PNG paths |
| `animal_prefabs` | `species_id`, `bundle`, `asset` | Optional Unity AssetBundle and lowercase prefab asset path |

`PRAGMA user_version` is `3`. Existing v1/v2 databases upgrade on their next save. Species ids are unique; a stat references its species
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
and a placeholder description. Missing translations fall back to English. Empty or
missing notes retain the template's notes. At runtime Tyrant creates separate PK
localization assets and follows the current game language; original CSVs are unchanged.
The editor provides key inputs, a language selector, and separate buttons to reveal
each text file. Legacy combined language files remain readable for compatibility,
but new species write one plain text file per field.

## Custom icons

Choose **Choose icon PNG…** in the species editor. Tyrant copies the PNG to
`icons/<species-id>.png` and records its path in `animal_assets` with `slot = 'icon'`.
It replaces that species' `heroRender`, used by the Nursery species button and info
preview. Transparent PNGs are supported. Skin swatches, animal-info-sign backgrounds,
and sex/growth portrait assets remain separate template assets. **Use template icon**
removes the override. SQL users may supply another relative PNG path inside the mod.
Language files and icon PNGs are included in mod exports and installs.

At runtime Tyrant reads `animals.db`, clones the template's animal data, applies
the overrides and registers the result in PK's in-memory animal database before
AI tables initialize. PK's game database files are not modified.

## CLI

```powershell
tyrant mod add-species my-animal-pack example-animal "Allosaurus Anax" --name "Example animal" --genus Example --epithet animal --cost 25000 -w <workspace>
tyrant mod show my-animal-pack -w <workspace>
tyrant mod check my-animal-pack -w <workspace>
```

Use the app or SQL for other stats. The CLI's `mod add-skin` and `mod replace-model`
can target the namespaced species identity and resolve its template prefab and maps.
