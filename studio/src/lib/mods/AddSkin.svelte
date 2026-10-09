<script lang="ts">
  import { keyOf } from './species';
  import { onMount } from 'svelte';
  import type { CustomSpeciesRow, ModRow, ModSpeciesResult } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';

  /** speciesId: an exact species id to preselect (wins over speciesKey). modId: add to this mod (the mod editor) instead of choosing one; onDone: called after adding, or on Cancel. */
  let { speciesKey = '', speciesId = '', modId, group = null, onDone }: { speciesKey?: string; speciesId?: string; modId?: string; group?: string | null; onDone?: () => void } = $props();

  const NEW = '__new__';
  const session = getSession();
  const tab = getTab();
  let data = $state<ModSpeciesResult | null>(null);
  let mods = $state<ModRow[]>([]);
  let custom = $state<CustomSpeciesRow[]>([]);
  let species = $state('');
  let name = $state('');
  let base = $state('');
  let male = $state(true);
  let female = $state(true);
  let maps = $state(false);
  let target = $state(NEW);
  let newId = $state('my-skins');
  let unmatched = $state(false);
  // Base game species plus the custom species mods add, so a skin can be added to either.
  const all = $derived([
    ...(data?.species ?? []),
    ...custom.map((c) => ({ speciesId: c.speciesId, vivarium: false, skins: c.skins, prefabGuid: null })),
  ]);
  const selected = $derived(all.find((s) => s.speciesId === species) ?? null);
  const baseSkin = $derived(selected?.skins.find((s) => s.name === base) ?? null);
  // A sex the base skin has no textures for cannot be templated: switch it off.
  $effect(() => {
    if (baseSkin && !baseSkin.male) male = false;
    if (baseSkin && !baseSkin.female) female = false;
  });

  onMount(async () => {
    // Fetched together so the form does not appear before the lists that preselect it are in.
    const [speciesResult, list, customRows] = await Promise.all([
      tab.quietly(() => session.rpc.call('mods.species')),
      tab.quietly(() => session.rpc.call('mods.list')),
      tab.quietly(() => session.rpc.call('mods.customSpecies', { id: modId ?? null })),
    ]);
    custom = customRows?.species ?? [];
    mods = (list?.mods ?? []).filter((m) => m.state !== 'gameOnly');
    data = speciesResult;
    target = modId ?? mods[0]?.id ?? NEW;
    // Never guess: a Species tab name that matches no species id leaves the choice to the user.
    const found = all.find((s) => keyOf(s.speciesId) === speciesKey);
    unmatched = !speciesId && !!speciesKey && !found && !!data?.hasDump;
    const match = speciesId ? all.find((s) => s.speciesId === speciesId) : speciesKey ? found : all[0];
    species = match?.speciesId ?? '';
    base = match?.skins[0]?.name ?? '';
  });

  function chooseSpecies(id: string) {
    species = id;
    base = all.find((s) => s.speciesId === id)?.skins[0]?.name ?? '';
  }

  async function add() {
    let id = target;
    if (id === NEW) {
      id = newId.trim();
      const created = await tab.safely(() => session.rpc.call('mods.create', { id, name: null, author: null }));
      if (!created) return;
      mods = created.mods.filter((m) => m.state !== 'gameOnly');
      target = id;
    }
    const r = await tab.safely(() =>
      session.rpc.call('mods.addSkin', { id, species, name: name.trim(), base, male, female, maps, ...(group ? { group } : {}) }),
    );
    if (!r) return;
    tab.info(
      modId
        ? `Added skin '${name.trim()}'. Replace its files on the skin's page, then Check and Install to game.`
        : `Added skin '${name.trim()}' to '${id}'. Edit its PNGs in the mod's skins folder, then Check and Install to game in the Mods tab.`,
    );
    onDone?.();
  }
</script>

<section class="add-skin" aria-label="Add a skin">
  <h3>Add a skin</h3>
  {#if data && !data.hasDump}
    <p class="hint">
      Adding a skin needs the game's data (species and their skins). On the Workspace tab, click <strong>Run data dump</strong>, then come back.
    </p>
  {:else if data}
    {#if group}<p class="hint">Adds it into {group}.</p>{/if}
    {#if unmatched && !species}<p class="warn">Tyrant could not match this species to the game data's species list; pick it below.</p>{/if}
    <div class="form">
      <label>Species
        <select aria-label="Species" value={species} onchange={(e) => chooseSpecies(e.currentTarget.value)}>
          {#if !species}<option value="" disabled>Pick a species…</option>{/if}
          <optgroup label="Base game">
            {#each data.species as s (s.speciesId)}<option value={s.speciesId}>{s.speciesId}{s.vivarium ? ' (vivarium)' : ''}</option>{/each}
          </optgroup>
          {#if custom.some((c) => c.own)}
            <optgroup label="This mod's species">
              {#each custom.filter((c) => c.own) as c (c.speciesId)}<option value={c.speciesId}>{c.name}</option>{/each}
            </optgroup>
          {/if}
          {#if custom.some((c) => !c.own)}
            <optgroup label="Other mods' species">
              {#each custom.filter((c) => !c.own) as c (c.speciesId)}
                <option value={c.speciesId}>{c.name} — {c.modName} ({c.installed ? (c.enabled ? 'installed' : 'installed, off') : 'workspace'})</option>
              {/each}
            </optgroup>
          {/if}
        </select>
      </label>
      <label>Skin name <input aria-label="Skin name" placeholder="e.g. Red spot" bind:value={name} /></label>
      <label>Base skin
        <select aria-label="Base skin" bind:value={base}>
          {#each selected?.skins ?? [] as s (s.index)}<option value={s.name}>{s.name}</option>{/each}
        </select>
      </label>
      <label><input type="checkbox" bind:checked={male} disabled={baseSkin ? !baseSkin.male : false} /> Male</label>
      <label><input type="checkbox" bind:checked={female} disabled={baseSkin ? !baseSkin.female : false} /> Female</label>
      <label><input type="checkbox" bind:checked={maps} /> Also normal, extra, pattern and fur maps</label>
      {#if !modId}
        <label>Mod
          <select aria-label="Mod" bind:value={target}>
            {#each mods as m (m.id)}<option value={m.id}>{m.name}</option>{/each}
            <option value={NEW}>New mod…</option>
          </select>
        </label>
        {#if target === NEW}<label>New mod id <input aria-label="New mod id" bind:value={newId} /></label>{/if}
      {/if}
      <button class="primary" onclick={add} disabled={!name.trim() || !species || (!male && !female) || session.busy}>Add skin</button>
      {#if onDone}<button onclick={onDone}>Cancel</button>{/if}
    </div>
    <p class="hint">
      Tyrant copies the base skin's textures into the mod as a template. Edit them, and anything you leave out (the other sex, infant
      textures, genetics colours) stays like the base skin. In game, genetics still tint your texture.
    </p>
  {:else}
    <p class="hint">Loading…</p>
  {/if}
</section>

<style>
  .form { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
</style>
