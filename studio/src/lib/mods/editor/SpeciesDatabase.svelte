<script lang="ts">
  import { onMount } from 'svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import { getTab } from '$lib/shell/tab.svelte';
  import type { ModAnimalDatabase, ModSpeciesTemplateDto, SpeciesSkinsRow } from '$lib/rpc/types.gen';
  import OpenInBlender from '$lib/blender/OpenInBlender.svelte';
  import type { ModDoc } from './modDoc.svelte';

  let { doc, onPrefab = () => {} }: { doc: ModDoc; onPrefab?: (target: string) => void } = $props();
  const session = getSession();
  const tab = getTab();
  let database = $state<ModAnimalDatabase | null>(null);
  let templates = $state<SpeciesSkinsRow[]>([]);
  let selected = $state('');
  let speciesId = $state('');
  let template = $state('');
  let name = $state('');
  let genus = $state('');
  let epithet = $state('');
  let cost = $state<number | undefined>(undefined);
  let values = $state<Record<string, number | undefined>>({});
  let dataValues = $state<Record<string, string>>({});
  let localization = $state<Record<string, string>>({});
  let textLanguage = $state('en-US');
  let search = $state('');
  let page = $state(0);
  let busy = $state(false);
  let failed = $state(false);
  let initialized = false;
  let generation = 0;
  const animal = $derived(database?.animals.find((a) => a.id === selected));
  const creating = $derived(!selected);
  const matchingFields = $derived((animal?.dataFields ?? []).filter(f => f.path.toLowerCase().includes(search.toLowerCase())));
  const visibleFields = $derived(matchingFields.slice(page * 60, (page + 1) * 60));
  $effect(() => { search; selected; page = 0; });

  onMount(async () => {
    const result = await tab.safely(() => session.rpc.call('mods.species'));
    templates = result?.species.filter((s) => !s.vivarium && s.skins.length > 0) ?? [];
    if (!template) template = templates[0]?.speciesId ?? '';
  });

  $effect(() => {
    if (doc.detail?.revision) void reload();
  });

  async function reload() {
    const current = ++generation;
    const result = await tab.safely(() => session.rpc.call('mods.animalDatabase', { id: doc.id }));
    if (current !== generation) return;
    failed = !result;
    if (!result) return;
    database = result;
    if (!initialized) { selected = result.animals[0]?.id ?? ''; initialized = true; }
    if (selected && !result.animals.some((a) => a.id === selected)) selected = '';
  }

  // Reloads after saves, undo, redo, or changes made outside this tab.
  $effect(() => {
    const source = animal;
    speciesId = source?.id ?? '';
    name = source?.name ?? '';
    genus = source?.genus ?? '';
    epithet = source?.epithet ?? '';
    cost = source?.cost ?? undefined;
    values = { ...(source?.stats ?? {}) };
    dataValues = Object.fromEntries((source?.dataFields ?? []).filter(f => f.editable).map(f => [f.path, f.value]));
    localization = { ...(source?.localization ?? {}) };
    if (source) template = source.base;
  });

  // A new species shows what it inherits from the chosen template, as an existing species does from its own.
  let templateStats = $state<ModSpeciesTemplateDto | null>(null);
  $effect(() => {
    const chosen = creating ? template : '';
    if (!chosen) {
      templateStats = null;
      return;
    }
    void tab.quietly(() => session.rpc.call('mods.speciesTemplate', { template: chosen })).then((r) => {
      if (creating && chosen === template) templateStats = r?.found ? r : null;
    });
  });
  const inheritedCost = $derived(creating ? (templateStats?.cost ?? null) : (animal?.inheritedCost ?? null));
  const inheritedStats = $derived<Record<string, number>>(creating ? (templateStats?.stats ?? {}) : (animal?.inheritedStats ?? {}));

  const validId = $derived(/^[a-z0-9](?:[a-z0-9-]{1,62})[a-z0-9]$/.test(speciesId));
  const canSave = $derived(!!name.trim() && !!genus.trim() && !!epithet.trim() && (!creating || (validId && !!template)));

  async function save() {
    busy = true;
    try {
      const stats = Object.fromEntries(Object.entries(values).filter((entry): entry is [string, number] => entry[1] !== undefined));
      const fields = { speciesId, name, genus, epithet, cost: cost ?? null, stats, dataFields: dataValues, localization };
      const added = creating;
      if (await doc.edit(added ? 'mods.addSpecies' : 'mods.setSpecies', added ? { ...fields, template } : fields, { undoable: false })) {
        selected = speciesId;
        await reload();
        tab.info(added ? `Added species '${name}'.` : `Saved '${name}' and its stats.`);
      }
    } finally { busy = false; }
  }
  async function pickIcon() {
    const png = await session.platform.openFile('Choose a species portrait PNG', ['png']);
    if (png) await doc.edit('mods.setSpeciesIcon', { speciesId: selected, png }, { undoable: false });
  }
</script>

<section class="database" aria-label="Species database">
  <h2>Species database</h2>
  <div><button disabled={!database?.animals.length} onclick={() => doc.detail && session.platform.reveal(`${doc.detail.dir}/animals.db`)}>Show database file</button></div>
  <p class="hint">Animals live in a separate SQLite file, animals.db. Edit it with SQL or a database editor, then return to this tab to reload.</p>
  <p class="hint">Each animal gets its own Nursery entry. Creation copies its template's serialized animal and linked asset fields. Blank basic stats inherit the template; the full record below includes AI, growth, social, combat, and other settings.</p>
  <p class="warn">Experimental: spawning and save/reload still need an in-game test. Keep the mod installed when loading parks containing its animals.</p>
  {#if failed}
    <p>Could not load the species database. <button onclick={reload}>Try again</button></p>
  {:else if database}
    <div class="toolbar">
      <label>Animal
        <select bind:value={selected} disabled={busy}>
          <option value="">New species…</option>
          {#each database.animals as entry (entry.id)}<option value={entry.id}>{entry.name}</option>{/each}
        </select>
      </label>
      <button onclick={() => (selected = '')} disabled={busy}>Add species</button>
    </div>
    <form onsubmit={(event) => { event.preventDefault(); void save(); }}>
      <fieldset disabled={busy}>
        <legend>{creating ? 'New animal' : animal?.name}</legend>
        <div class="grid">
          <label>Species id <input bind:value={speciesId} readonly={!creating} placeholder="my-new-animal" required /></label>
          {#if creating}
            <label>Template
              <select bind:value={template} required>
                {#each templates as source}<option value={source.speciesId}>{source.speciesId}</option>{/each}
              </select>
            </label>
          {:else}<p class="hint">Template: {animal?.base}</p>{/if}
          <label>Display name <input bind:value={name} required /></label>
          <label>Genus <input bind:value={genus} required /></label>
          <label>Species epithet <input bind:value={epithet} required /></label>
          <label>Purchase cost
            <input type="number" min="0" max="2147483647" step="1" bind:value={cost} placeholder={inheritedCost?.toString() ?? 'Template value'} />
            {#if inheritedCost != null}<span class="hint">Template: {inheritedCost}</span>{/if}
          </label>
        </div>
        <h3>Animal stats</h3>
        <div class="grid">
          {#each database.fields as field (field.id)}
            <label>{field.label} <span class="unit">({field.unit})</span>
              <input type="number" min={field.min} max={field.max} step="any" bind:value={values[field.id]}
                placeholder={inheritedStats[field.id]?.toString() ?? 'Template value'} />
              {#if inheritedStats[field.id] != null}<span class="hint">Template: {inheritedStats[field.id]}</span>{/if}
            </label>
          {/each}
        </div>
        {#if !creating}
          <h3>Species icon</h3>
          <p class="hint">{animal?.icon ?? 'Using the template portrait'}. A PNG replaces this species' Nursery portrait.</p>
          <div class="toolbar">
            <button type="button" onclick={pickIcon}>Choose icon PNG…</button>
            <button type="button" disabled={!animal?.icon} onclick={() => doc.edit('mods.setSpeciesIcon', { speciesId: selected, png: null }, { undoable: false })}>Use template icon</button>
          </div>
          <h3>Localization keys</h3>
          <p class="hint">Each key points to its own plain text file. For example, mirabilis/description reads localization/en-US/mirabilis/description.txt. Name, description, character notes and social info are separate files. Missing translations fall back to English.</p>
          <label>Text language <select bind:value={textLanguage}>{#each ['en-US', 'fr', 'es', 'pt', 'de', 'ru', 'zh-CN'] as code}<option value={code}>{code}</option>{/each}</select></label>
          <div class="grid">
            {#each ['name', 'description', 'characterNotes', 'socialInfo'] as slot}
              <label>{slot} key <input bind:value={localization[slot]} placeholder={`mirabilis/${slot}`} /></label>
              <button type="button" disabled={!localization[slot]} onclick={() => doc.detail && session.platform.reveal(`${doc.detail.dir}/localization/${textLanguage}/${localization[slot].replaceAll('\\', '/')}.txt`)}>Show {slot} text file</button>
            {/each}
          </div>
          <h3>Full animal record</h3>
          <p class="hint">Stored in animal_fields. Edit numbers, booleans, enum names and text here or with SQL. Paths follow the game's fields; reference and structural rows describe inherited assets and arrays. Purchase cost is in animal_species; the basic stats above are in animal_stats.</p>
          <label>Search fields <input bind:value={search} placeholder="brain/needs, social, diet, growth…" /></label>
          <p class="hint">{matchingFields.length} fields</p>
          <div class="record">
            {#each visibleFields as field (field.path)}
              <label class="field"><span class="path">{field.path}</span>
                {#if field.editable}
                  {#if field.kind === 'boolean'}
                    <select bind:value={dataValues[field.path]}><option value="true">true</option><option value="false">false</option></select>
                  {:else}<input bind:value={dataValues[field.path]} aria-label={field.path} />{/if}
                  <span class="hint">Template: {field.templateValue}</span>
                {:else}<span class="hint reference">{field.kind}: {field.value}</span>{/if}
              </label>
            {/each}
          </div>
          <div class="toolbar">
            <button type="button" disabled={page === 0} onclick={() => page--}>Previous fields</button>
            <span class="hint">Page {page + 1} / {Math.max(1, Math.ceil(matchingFields.length / 60))}</span>
            <button type="button" disabled={(page + 1) * 60 >= matchingFields.length} onclick={() => page++}>Next fields</button>
          </div>
        {/if}
        <div class="actions">
          <button type="submit" class="primary" disabled={!canSave}>{busy ? 'Saving…' : creating ? 'Create species' : 'Save species'}</button>
          <button type="button" onclick={() => { values = {}; cost = undefined; dataValues = Object.fromEntries((animal?.dataFields ?? []).filter(f => f.editable).map(f => [f.path, f.templateValue])); }}>Reset stats to template</button>
        </div>
      </fieldset>
    </form>
    {#if animal && !creating}
      <h3>Prefab and look</h3>
      <div class="toolbar">
        <button type="button" onclick={() => onPrefab(animal.key)}>Prefab settings</button>
        <OpenInBlender species={animal.key} mod={doc.id} />
        {#if animal.prefab}
          <button type="button" onclick={() => doc.edit('mods.setSpeciesPrefab', { speciesId: selected, bundle: null, asset: null }, { undoable: false })}>Use template prefab</button>
          <span class="hint">This species loads a Unity bundle ({animal.prefab.bundle}); the Unity path is parked.</span>
        {/if}
      </div>
    {/if}
    {#if creating && !templates.length}<p class="hint">Run the game's data dump on the Workspace tab to choose a template.</p>{/if}
  {:else}<p class="hint">Loading species…</p>{/if}
</section>

<style>
  .database { max-width: 900px; display: grid; gap: 12px; }
  h2, p { margin: 0; }
  .toolbar, .actions { display: flex; flex-wrap: wrap; gap: 12px; align-items: end; }
  .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); gap: 14px; }
  fieldset { border: 1px solid var(--border); border-radius: var(--radius); padding: 16px; }
  legend { padding: 0 6px; }
  label { display: grid; gap: 4px; color: var(--muted); }
  input, select { font: inherit; background: var(--bg); color: var(--text); border: 1px solid var(--border); border-radius: 6px; padding: 7px 9px; width: 100%; box-sizing: border-box; }
  .unit, .hint { font-size: 12px; }
  .actions { margin-top: 18px; }
  .record { display: grid; gap: 12px; margin: 12px 0; }
  .path { font-family: monospace; overflow-wrap: anywhere; color: var(--text); }
  .reference { overflow-wrap: anywhere; white-space: pre-wrap; }
</style>
