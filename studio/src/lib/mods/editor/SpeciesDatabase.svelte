<script lang="ts">
  import PortraitsCard from './PortraitsCard.svelte';
  import RemoveSpeciesDialog from './RemoveSpeciesDialog.svelte';
  import StartingSkinChoice, { freshChoice, startParams, startingSkinOf, type StartingChoice } from './StartingSkinChoice.svelte';
  import { onMount, untrack } from 'svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import { getTab } from '$lib/shell/tab.svelte';
  import type { ModAnimalDatabase, ModSpeciesTemplateDto, SpeciesRemoval, SpeciesSkinsRow } from '$lib/rpc/types.gen';
  import OpenInBlender from '$lib/blender/OpenInBlender.svelte';
  import RecordTree from './RecordTree.svelte';
  import SizeCard from './SizeCard.svelte';
  import SpeciesSoundsCard from './SpeciesSoundsCard.svelte';
  import Thumb from './Thumb.svelte';
  import type { AddedSound } from '$lib/sounds/audio';
  import type { ModDoc } from './modDoc.svelte';

  let { doc, onPrefab = () => {}, onAddSkin = () => {}, onOpenSkin = () => {}, onAddSound = () => {}, onOpenSound = () => {} }: {
    doc: ModDoc; onPrefab?: (target: string) => void; onAddSkin?: (species: string) => void; onOpenSkin?: (id: string) => void;
    /** The species page's Sounds: + Add (the sound picker on this species) and Edit (a replacement's own page). */
    onAddSound?: (species: string) => void; onOpenSound?: (sound: AddedSound) => void;
  } = $props();
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
  /** The new species' starting skin (create form), and the one being made for a species without one (Skins section). */
  let choice = $state<StartingChoice>(freshChoice(''));
  let making = $state<StartingChoice | null>(null);
  let cost = $state<number | undefined>(undefined);
  let values = $state<Record<string, number | undefined>>({});
  let dataValues = $state<Record<string, string>>({});
  let localization = $state<Record<string, string>>({});
  let textLanguage = $state('en-US');
  /** Each language's Paleopedia texts as edited (language code → field → text); an empty translation uses the English text in game. */
  let texts = $state<Record<string, Record<string, string>>>({});
  /** The texts as last loaded and whose they are: a reload keeps what was typed since, unless it is another species or this page saved. */
  let loadedTexts: Record<string, Record<string, string>> = {};
  let textsOf = '';
  let savedTexts = false;
  let busy = $state(false);
  let failed = $state(false);
  let initialized = false;
  let generation = 0;
  const animal = $derived(database?.animals.find((a) => a.id === selected));
  const creating = $derived(!selected);
  const templateSkins = $derived(templates.find((t) => t.speciesId === (creating ? template : animal?.base))?.skins ?? []);

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
    const server = source?.texts ?? {};
    untrack(() => {
      const fresh = !source || source.id !== textsOf || savedTexts;
      texts = Object.fromEntries(Object.entries(server).map(([code, slots]) => [code, Object.fromEntries(Object.entries(slots).map(([slot, text]) => {
        const typed = texts[code]?.[slot];
        return [slot, !fresh && typed !== undefined && typed !== (loadedTexts[code]?.[slot] ?? '') ? typed : text];
      }))]));
      loadedTexts = server;
      textsOf = source?.id ?? '';
      savedTexts = false;
    });
    if (source) template = source.base;
  });

  // A different template has different skins, so the starting skin goes back to copying its first.
  $effect(() => {
    choice = freshChoice(template);
  });
  // Another animal: no half-made starting skin carries over.
  $effect(() => {
    void selected;
    making = null;
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
  const canSave = $derived(!!name.trim() && !!genus.trim() && !!epithet.trim() && (!creating || (validId && !!template && !(choice.kind === 'png' && !choice.male))));
  /** This mod's starting skin of the shown species, when it has one. */
  const startingSkin = $derived(animal?.startingSkinId ? doc.detail?.skins.find((s) => s.id === animal.startingSkinId) : undefined);

  const languages: [string, string][] = [['en-US', 'English'], ['fr', 'French'], ['es', 'Spanish'], ['pt', 'Portuguese'], ['de', 'German'], ['ru', 'Russian'], ['zh-CN', 'Chinese']];
  const textFields: [string, string][] = [['description', 'Description'], ['characterNotes', 'Character notes'], ['socialInfo', 'Social info']];
  const translated = (code: string) => Object.values(texts[code] ?? {}).some((text) => text.trim() !== '');
  /** The texts that differ from the files, without the English name (that is the Display name). */
  function changedTexts(): Record<string, Record<string, string>> | null {
    const changed: Record<string, Record<string, string>> = {};
    for (const [code, slots] of Object.entries(texts))
      for (const [slot, text] of Object.entries(slots))
        if (!(code === 'en-US' && slot === 'name') && text !== (animal?.texts?.[code]?.[slot] ?? '')) (changed[code] ??= {})[slot] = text;
    return Object.keys(changed).length ? changed : null;
  }
  function setText(code: string, slot: string, text: string) {
    texts[code] = { ...(texts[code] ?? {}), [slot]: text };
  }

  async function save() {
    busy = true;
    try {
      const stats = Object.fromEntries(Object.entries(values).filter((entry): entry is [string, number] => entry[1] !== undefined));
      const fields = { speciesId, name, genus, epithet, cost: cost ?? null, stats, dataFields: dataValues, localization };
      const added = creating;
      const start = { startingSkin: startingSkinOf(choice, template), ...startParams(choice) };
      if (await doc.edit(added ? 'mods.addSpecies' : 'mods.setSpecies', added ? { ...fields, template, ...start } : { ...fields, texts: changedTexts() }, { undoable: false })) {
        savedTexts = true;
        selected = speciesId;
        await reload();
        tab.info(added ? `Added species '${name}'.` : `Saved '${name}' and its stats.`);
      }
    } finally { busy = false; }
  }
  async function makeStartingSkin() {
    if (!making || !animal) return;
    if (await doc.edit('mods.addStartingSkin', { speciesId: selected, ...startParams(making) }, { undoable: false })) making = null;
  }
  function setStart(skin: number, skinName: string | null) {
    void doc.edit('mods.setSpeciesStartingSkin', { speciesId: selected, startingSkin: skin, name: skinName }, { undoable: false });
  }
  async function pickSwatch() {
    const png = await session.platform.openFile('Choose a swatch PNG for the starting skin', ['png']);
    if (png) await doc.edit('mods.setSpeciesSwatch', { speciesId: selected, png }, { undoable: false });
  }
  /** What deleting the shown species takes with it (mods.speciesRemoval), while its dialog is open. */
  let removal = $state<SpeciesRemoval | null>(null);
  async function askDelete() {
    removal = await tab.safely(() => session.rpc.call('mods.speciesRemoval', { id: doc.id, speciesId: selected })) ?? null;
  }
  async function deleteSpecies(deleteFiles: boolean) {
    const gone = selected;
    removal = null;
    if (await doc.edit('mods.removeSpecies', { speciesId: gone, deleteFiles }, { undoable: false })) {
      selected = database?.animals.find((a) => a.id !== gone)?.id ?? '';
      tab.info(`Deleted species '${gone}'.`);
    }
  }
  async function makeDefault(skin: string, name: string) {
    const current = startingSkin?.name;
    const message = `Make ${name} the starting skin of ${animal?.name}? It becomes the look the species starts with (skin 0).`
      + (current ? ` ${current} stays as an ordinary skin; what ${name} took from it (PNGs, colours, portraits, swatch) is copied into ${name} first.` : '')
      + ` Skins based on 0 take what they have no PNG for from ${name}, and animals with skin 0 in saved parks will wear it.`;
    if (!(await session.platform.confirm(message, 'Make default'))) return;
    await doc.edit('mods.makeStartingSkin', { skin });
  }
  async function pickIcon() {
    const png = await session.platform.openFile('Choose a Nursery picture PNG (512×256)', ['png']);
    if (png) await doc.edit('mods.setSpeciesIcon', { speciesId: selected, png }, { undoable: false });
  }
  async function pickPaleopediaPicture() {
    const png = await session.platform.openFile('Choose a Paleopedia picture PNG (16:9; the game uses 2048×1152)', ['png']);
    if (png) await doc.edit('mods.setSpeciesPaleopediaPicture', { speciesId: selected, png }, { undoable: false });
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
        {#if creating && template}
          <StartingSkinChoice {templates} {template} speciesName={name} value={choice} onChange={(c) => (choice = c)} />
          <p class="hint">The starting skin becomes a skin in this mod: its PNGs, colours, model and portraits are yours to edit on its page.</p>
        {/if}
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
          <section class="texts" aria-label="Paleopedia text">
            <h3>Paleopedia text</h3>
            <p class="hint">What the species' Paleopedia page says. Every language without its own text uses the English, so write English first and translate later.</p>
            <label class="language">Language
              <select bind:value={textLanguage}>
                {#each languages as [code, label] (code)}<option value={code}>{code === 'en-US' ? label : `${label} (${translated(code) ? 'translated' : 'uses English'})`}</option>{/each}
              </select>
            </label>
            {#if textLanguage === 'en-US'}
              <p class="hint">The English name is the Display name above. Empty character notes or social info show the template's.</p>
            {:else}
              <div class="text-field">
                <label>Name <input value={texts[textLanguage]?.name ?? ''} placeholder={name} oninput={(e) => setText(textLanguage, 'name', e.currentTarget.value)} /></label>
                {@render fallback('name', 'Name')}
              </div>
            {/if}
            {#each textFields as [slot, label] (slot)}
              <div class="text-field">
                <label>{label}
                  <textarea rows={slot === 'description' ? 6 : 3} value={texts[textLanguage]?.[slot] ?? ''}
                    placeholder={textLanguage === 'en-US' ? '' : (texts['en-US']?.[slot] ?? '')}
                    oninput={(e) => setText(textLanguage, slot, e.currentTarget.value)}></textarea>
                </label>
                {#if textLanguage !== 'en-US'}{@render fallback(slot, label)}{/if}
              </div>
            {/each}
          </section>
          {#snippet fallback(slot: string, label: string)}
            {#if (texts[textLanguage]?.[slot] ?? '').trim() === ''}<span class="hint">Uses English</span>
            {:else}<span><button type="button" class="small" aria-label="{label}: Use English" onclick={() => setText(textLanguage, slot, '')}>Use English</button></span>{/if}
          {/snippet}
          <details class="advanced">
            <summary><h3>Text files</h3> <span class="hint">Advanced: where each text is stored.</span></summary>
            <p class="hint">Each key points to its own plain text file. For example, mirabilis/description reads localization/en-US/mirabilis/description.txt. The Show buttons open the file of the language picked above.</p>
            <div class="grid">
              {#each ['name', 'description', 'characterNotes', 'socialInfo'] as slot}
                <label>{slot} key <input bind:value={localization[slot]} placeholder={`mirabilis/${slot}`} /></label>
                <button type="button" disabled={!localization[slot]} onclick={() => doc.detail && session.platform.reveal(`${doc.detail.dir}/localization/${textLanguage}/${localization[slot].replaceAll('\\', '/')}.txt`)}>Show {slot} text file</button>
              {/each}
            </div>
          </details>
          <details class="advanced">
            <summary><h3>Full animal record</h3> <span class="hint">Advanced: every field the game stores for this animal. Most species never need it.</span></summary>
            <p class="hint">Stored in animal_fields. Edit numbers, booleans, enum names and text here or with SQL. Paths follow the game's fields; reference and structural rows describe inherited assets and arrays. Purchase cost is in animal_species; the basic stats above are in animal_stats.</p>
            <RecordTree fields={animal?.dataFields ?? []} bind:values={dataValues} />
          </details>
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
      <h3>Size</h3>
      <SizeCard {doc} target={animal.key} />
      <SpeciesSoundsCard {doc} species={animal.key} onOpen={onOpenSound} onAdd={() => onAddSound(animal.key)} />
      <h3>Nursery picture</h3>
      <p class="hint">The picture on its Nursery card and the Nursery's species panel. {animal.icon ? `Using ${animal.icon}.` : "Using the template's picture."} The game's are 512×256 PNGs.</p>
      {#if animal.icon}<Thumb modId={doc.id} file={animal.icon} size={256} aspect={2} label="Nursery picture of {animal.name}" version="{doc.detail?.revision}|{doc.detail?.filesStamp}" />{/if}
      <div class="toolbar">
        <button type="button" aria-label="Nursery picture: Choose PNG…" onclick={pickIcon}>Choose PNG…</button>
        <button type="button" aria-label="Nursery picture: Use template picture" disabled={!animal.icon} onclick={() => doc.edit('mods.setSpeciesIcon', { speciesId: selected, png: null }, { undoable: false })}>Use template picture</button>
      </div>
      <h3>Paleopedia picture</h3>
      <p class="hint">The big picture on the species' own Paleopedia page (also its row in the Paleopedia's animal list, and its notifications). {animal.paleopediaPicture ? `Using ${animal.paleopediaPicture}.` : "Using the template's picture."} The game's are 2048×1152 (16:9) PNGs.</p>
      {#if animal.paleopediaPicture}<Thumb modId={doc.id} file={animal.paleopediaPicture} size={256} aspect={16 / 9} label="Paleopedia picture of {animal.name}" version="{doc.detail?.revision}|{doc.detail?.filesStamp}" />{/if}
      <div class="toolbar">
        <button type="button" aria-label="Paleopedia picture: Choose PNG…" onclick={pickPaleopediaPicture}>Choose PNG…</button>
        <button type="button" aria-label="Paleopedia picture: Use template picture" disabled={!animal.paleopediaPicture} onclick={() => doc.edit('mods.setSpeciesPaleopediaPicture', { speciesId: selected, png: null }, { undoable: false })}>Use template picture</button>
      </div>
    {/if}
    {#if animal && !creating}
      <section class="skins" aria-label="Skins">
        <h3>Skins</h3>
        {#if startingSkin}
          <p class="hint">The species starts with its own skin; edit its PNGs, name, swatch and portraits on its page. Add more skins below; other mods can add skins to it too.</p>
          <div class="toolbar">
            <span>Starting skin: {startingSkin.name}</span>
            <button type="button" aria-label="Open starting skin" onclick={() => onOpenSkin(startingSkin.id)}>Open</button>
          </div>
        {:else}
        <p class="hint">The species starts with one skin copied from {animal.base}. Add more skins below; other mods can add skins to it too.</p>
        <div class="grid">
          <label>Starting skin
            <select value={animal.startingSkin ?? 0} onchange={(e) => setStart(Number(e.currentTarget.value), animal.startingSkinName ?? null)}>
              {#each templateSkins as s (s.index)}<option value={s.index}>{s.name}</option>{/each}
            </select>
          </label>
          <label>Starting skin name
            <input value={animal.startingSkinName ?? ''} placeholder={templateSkins.find((s) => s.index === (animal.startingSkin ?? 0))?.name ?? ''}
              onchange={(e) => setStart(animal.startingSkin ?? 0, e.currentTarget.value.trim() || null)} />
          </label>
        </div>
        <div class="toolbar">
          <button type="button" onclick={pickSwatch}>Choose swatch PNG…</button>
          <button type="button" disabled={!animal.swatch} onclick={() => doc.edit('mods.setSpeciesSwatch', { speciesId: selected, png: null }, { undoable: false })}>Use template swatch</button>
        </div>
        <PortraitsCard {doc} target={{ speciesId: selected, speciesKey: animal.key }} />
        {#if making}
          <StartingSkinChoice {templates} template={animal.base} speciesName={animal.name} value={making} onChange={(c) => (making = c)} />
          <div class="toolbar">
            <button type="button" class="primary" disabled={making.kind === 'png' && !making.male} onclick={makeStartingSkin}>Create starting skin</button>
            <button type="button" onclick={() => (making = null)}>Cancel</button>
          </div>
        {:else}
          <div><button type="button" onclick={() => (making = freshChoice(animal.base, animal.startingSkin ?? 0))}>Make an editable starting skin</button></div>
        {/if}
        {/if}
        <ul class="skin-list">
          {#each (doc.detail?.skins ?? []).filter((s) => s.species === animal.key) as s (s.id)}
            <li><button type="button" onclick={() => onOpenSkin(s.id)}>{s.name}</button>{#if s.starting}<span class="hint"> · starting skin</span>{:else}
              <button type="button" class="small" aria-label="Make {s.name} the default" title="Make it the species' starting (default) skin" onclick={() => makeDefault(s.id, s.name)}>Make default</button>{/if}{#if s.group}<span class="hint"> · {s.group}</span>{/if}</li>
          {/each}
        </ul>
        <div><button type="button" onclick={() => onAddSkin(animal.key)}>Add skin</button></div>
      </section>
      <section class="danger" aria-label="Delete species">
        <h3>Delete species</h3>
        <p class="hint">Removes {animal.name} from this mod with its skins, models, sounds and prefab settings.</p>
        <div><button type="button" disabled={busy} onclick={askDelete}>Delete species…</button></div>
      </section>
    {/if}
    {#if removal}
      <RemoveSpeciesDialog plan={removal} onConfirm={deleteSpecies} onCancel={() => (removal = null)} />
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
  .texts { display: grid; gap: 10px; margin-top: 12px; }
  .texts .language { max-width: 260px; }
  .text-field { display: grid; gap: 4px; justify-items: start; }
  .text-field label { width: 100%; }
  textarea { resize: vertical; }
  input, select, textarea { font: inherit; background: var(--bg); color: var(--text); border: 1px solid var(--border); border-radius: 6px; padding: 7px 9px; width: 100%; box-sizing: border-box; }
  .unit, .hint { font-size: 12px; }
  .actions { margin-top: 18px; }
  .skins { display: grid; gap: 12px; }
  .skin-list { margin: 0; padding-left: 18px; }
  .skin-list li { margin: 4px 0; }
  .skin-list .small { margin-left: 8px; padding: 2px 8px; font-size: 0.85em; }
  .advanced { margin-top: 12px; display: grid; gap: 8px; }
  .advanced summary { cursor: pointer; }
  .advanced summary h3 { display: inline; }
  .danger { display: grid; gap: 8px; border-top: 1px solid var(--border); padding-top: 12px; }
</style>
