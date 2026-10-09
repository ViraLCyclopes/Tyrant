<script lang="ts">
  import { onMount } from 'svelte';
  import type { ModAnimalDatabase, ModSpeciesResult } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { ModDoc } from './modDoc.svelte';

  /** The mod editor's Prefab settings → + Add: pick the species (this mod's or the game's) whose prefab to adjust. */
  let { doc, onDone }: { doc: Pick<ModDoc, 'id'>; onDone: (target: string | null) => void } = $props();

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
  <h2>Add prefab settings</h2>
  {#if !loaded}
    <p class="hint">Loading the species…</p>
  {:else}
    <div class="row">
      <label>
        Species
        <select aria-label="Species" bind:value={target}>
          <optgroup label="This mod's species">
            {#each mine as key (key)}<option value={key}>{key}</option>{/each}
          </optgroup>
          <optgroup label="Game species">
            {#each game as id (id)}<option value={id}>{id}</option>{/each}
          </optgroup>
        </select>
      </label>
    </div>
    <div class="row">
      <button class="primary" disabled={!target} onclick={() => onDone(target)}>Open</button>
      <button onclick={() => onDone(null)}>Cancel</button>
    </div>
    <p class="hint">Opens the animal's prefab fields. Nothing is saved until you change one.</p>
  {/if}
</section>

<style>
  .row { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; margin: 8px 0; }
</style>
