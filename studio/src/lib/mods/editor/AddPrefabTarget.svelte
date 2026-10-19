<script lang="ts">
  import { onMount } from 'svelte';
  import type { ModAnimalDatabase, ModSpeciesResult } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { ModDoc } from './modDoc.svelte';

  /**
   * The mod editor's Prefab settings → + Add: pick the species (this mod's or the game's) whose prefab to adjust. Species
   * materials → + Add uses it too, with its own words.
   */
  let {
    doc,
    onDone,
    title = 'Add prefab settings',
    what = 'Prefab settings',
    opens = "the animal's prefab fields",
  }: { doc: Pick<ModDoc, 'id'>; onDone: (target: string | null) => void; title?: string; what?: string; opens?: string } = $props();

  const session = getSession();
  const tab = getTab();
  let species = $state.raw<ModSpeciesResult | null>(null);
  let animals = $state.raw<ModAnimalDatabase | null>(null);
  let loaded = $state(false);
  let target = $state('');

  const mine = $derived(animals?.animals.map((a) => a.key) ?? []);
  const game = $derived(species?.species.filter((s) => !s.vivarium).map((s) => s.speciesId) ?? []);

  onMount(async () => {
    [species, animals] = await Promise.all([
      tab.quietly(() => session.rpc.call('mods.species')),
      tab.quietly(() => session.rpc.call('mods.animalDatabase', { id: doc.id })),
    ]);
    target = mine[0] ?? game[0] ?? '';
    loaded = true;
  });
</script>

<section class="card">
  <h2>{title}</h2>
  {#if !loaded}
    <p class="hint">Loading the species…</p>
  {:else if species && !species.hasDump && mine.length === 0}
    <p class="hint">{what} for game species need the game's data: on the Workspace tab click Run data dump, then try again.</p>
    <div class="row"><button onclick={() => onDone(null)}>Cancel</button></div>
  {:else}
    {#if species && !species.hasDump}
      <p class="hint">The game's species need the game's data: on the Workspace tab click Run data dump.</p>
    {/if}
    <div class="row">
      <label>
        Species
        <select aria-label="Species" bind:value={target}>
          {#if mine.length}
            <optgroup label="This mod's species">
              {#each mine as key (key)}<option value={key}>{key}</option>{/each}
            </optgroup>
          {/if}
          {#if game.length}
            <optgroup label="Game species">
              {#each game as id (id)}<option value={id}>{id}</option>{/each}
            </optgroup>
          {/if}
        </select>
      </label>
    </div>
    <div class="row">
      <button class="primary" disabled={!target} onclick={() => onDone(target)}>Open</button>
      <button onclick={() => onDone(null)}>Cancel</button>
      {#if !target}<span class="hint">There are no species to choose from.</span>{/if}
    </div>
    <p class="hint">Opens {opens}. Nothing is saved until you change one.</p>
  {/if}
</section>

<style>
  .row { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; margin: 8px 0; }
</style>
