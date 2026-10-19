<script lang="ts">
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import AddPrefabTarget from './AddPrefabTarget.svelte';
  import type { ModDoc } from './modDoc.svelte';

  /** The mod editor's Vanilla skin materials → + Add: pick the species (as for species materials), then one of its game skins. */
  let { doc, onDone }: { doc: Pick<ModDoc, 'id'>; onDone: (pick: { species: string; skin: string } | null) => void } = $props();
  const session = getSession();
  const tab = getTab();
  let species = $state<string | null>(null);
  let names = $state.raw<string[] | null>(null);
  let skin = $state('');

  // The chosen species' game skins.
  $effect(() => {
    const chosen = species;
    names = null;
    if (!chosen) return;
    void tab.quietly(() => session.rpc.call('mods.vanillaSkins', { id: doc.id, species: chosen })).then((r) => {
      if (species !== chosen) return;
      names = r?.names ?? [];
      skin = names[0] ?? '';
    });
  });
</script>

{#if !species}
  <AddPrefabTarget {doc} onDone={(s) => (s ? (species = s) : onDone(null))} title="Add vanilla skin materials" what="Vanilla skin materials" opens="that skin's material settings" />
{:else}
  <section class="card">
    <h2>Add vanilla skin materials: {species}</h2>
    {#if !names}
      <p class="hint">Loading the skins…</p>
    {:else}
      <div class="row">
        <label>
          Vanilla skin
          <select aria-label="Vanilla skin" bind:value={skin}>
            {#each names as n (n)}<option value={n}>{n}</option>{/each}
          </select>
        </label>
      </div>
      <div class="row">
        <button class="primary" disabled={!skin} onclick={() => onDone({ species: species!, skin })}>Open</button>
        <button onclick={() => (species = null)}>Back</button>
        <button onclick={() => onDone(null)}>Cancel</button>
        {#if !names.length}<span class="hint">{species} has no game skins to choose from.</span>{/if}
      </div>
      <p class="hint">Opens that skin's material settings. Nothing is saved until you change one.</p>
    {/if}
  </section>
{/if}

<style>
  .row { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; margin: 8px 0; }
</style>
