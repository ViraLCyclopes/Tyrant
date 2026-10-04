<script lang="ts">
  import { onMount } from 'svelte';
  import type { ModSpeciesResult } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import AllSounds from '$lib/sounds/AllSounds.svelte';
  import type { AddedSound } from '$lib/sounds/audio';
  import SpeciesSounds from '$lib/sounds/SpeciesSounds.svelte';

  /** The mod editor's Sounds → + Add: a species' sounds (or every game sound), each with Replace… into this mod. */
  let { modId, onDone }: { modId: string; onDone: (added: AddedSound | null) => void } = $props();

  const ALL = '__all__';
  const session = getSession();
  const tab = getTab();
  let data = $state.raw<ModSpeciesResult | null>(null);
  let source = $state('');

  onMount(async () => {
    data = await tab.quietly(() => session.rpc.call('mods.species'));
  });
</script>

<section class="card">
  <h2>Add a sound</h2>
  {#if !data}
    <p class="hint">Loading the species…</p>
  {:else if !data.hasDump}
    <p class="hint">The sound lists need the game's data: on the Workspace tab click Run data dump, then try again.</p>
  {:else}
    <label>
      Sounds of
      <select aria-label="Sounds of" bind:value={source}>
        <option value="" disabled>Pick a species…</option>
        {#each data.species as s (s.speciesId)}<option value={s.speciesId}>{s.speciesId}</option>{/each}
        <option value={ALL}>All sounds (buttons, buildings, music…)</option>
      </select>
    </label>
    {#if source === ALL}
      <AllSounds {modId} onAdded={onDone} />
    {:else if source}
      {#key source}<SpeciesSounds speciesKey={source} displayName={source} {modId} onAdded={onDone} />{/key}
    {/if}
  {/if}
  <div class="row"><button onclick={() => onDone(null)}>Cancel</button></div>
</section>

<style>
  .row { display: flex; gap: 8px; margin-top: 8px; }
</style>
