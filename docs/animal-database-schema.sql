-- SQLite animal database v4. Save this database as animals.db alongside mod.json.
PRAGMA foreign_keys = ON;
BEGIN;
CREATE TABLE animal_species (
    id TEXT PRIMARY KEY NOT NULL,
    template TEXT NOT NULL,
    name TEXT NOT NULL,
    genus TEXT NOT NULL,
    epithet TEXT NOT NULL,
    cost INTEGER CHECK(cost IS NULL OR (cost >= 0 AND cost <= 2147483647))
);
CREATE TABLE animal_stats (
    species_id TEXT NOT NULL REFERENCES animal_species(id) ON DELETE CASCADE,
    stat TEXT NOT NULL,
    value REAL NOT NULL,
    PRIMARY KEY(species_id, stat)
);
CREATE TABLE animal_fields (
    species_id TEXT NOT NULL REFERENCES animal_species(id) ON DELETE CASCADE,
    path TEXT NOT NULL,
    kind TEXT NOT NULL,
    value TEXT NOT NULL,
    template_value TEXT NOT NULL,
    editable INTEGER NOT NULL CHECK(editable IN (0,1)),
    PRIMARY KEY(species_id,path)
);
CREATE TABLE animal_localization (
    species_id TEXT NOT NULL REFERENCES animal_species(id) ON DELETE CASCADE,
    slot TEXT NOT NULL,
    term TEXT NOT NULL,
    PRIMARY KEY(species_id,slot)
);
CREATE TABLE animal_assets (
    species_id TEXT NOT NULL REFERENCES animal_species(id) ON DELETE CASCADE,
    slot TEXT NOT NULL CHECK(slot='icon'),
    file TEXT NOT NULL,
    PRIMARY KEY(species_id,slot)
);
CREATE TABLE animal_prefabs (
    species_id TEXT PRIMARY KEY NOT NULL REFERENCES animal_species(id) ON DELETE CASCADE,
    bundle TEXT NOT NULL,
    asset TEXT NOT NULL
);
CREATE TABLE animal_starting_skin (
    species_id TEXT PRIMARY KEY NOT NULL REFERENCES animal_species(id) ON DELETE CASCADE,
    skin INTEGER NOT NULL CHECK(skin >= 0),
    name TEXT,
    swatch TEXT
);
CREATE TABLE animal_portraits (
    species_id TEXT NOT NULL REFERENCES animal_species(id) ON DELETE CASCADE,
    slot TEXT NOT NULL,
    file TEXT NOT NULL,
    PRIMARY KEY(species_id,slot)
);
PRAGMA user_version = 5;
COMMIT;
