<script lang="ts">
  import { onMount } from 'svelte';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import { blockedReason, getBlender } from './blender.svelte';

  /**
   * Open in Blender for a game species (skins: its game skins to pick from), a mod skin (mod + skin) or a mod's species
   * model (mod, no skin). Start fresh rebuilds the Blender project; far LODs come along on request.
   */
  let { species, skin = undefined, mod = undefined, skins = undefined }: { species: string; skin?: string; mod?: string; skins?: string[] } = $props();
  const session = getSession();
  const tab = getTab();
  const blender = getBlender(session);
  let fresh = $state(false);
  let lods = $state(false);
  let sex = $state<'male' | 'female'>('male');
  let chosen = $state<string | null>(null);
  const reason = $derived(blockedReason(blender.status));

  onMount(() => void blender.ensure(tab));

  function open() {
    const gameSkin = skins ? (chosen ?? skins[0] ?? null) : null;
    void blender.open(tab, { species, skin: mod ? (skin ?? null) : gameSkin, mod: mod ?? null, fresh, lods, sex });
    fresh = false; // once: the next open must not set the .blend aside again
  }
</script>

<div class="open-in-blender">
  {#if skins && skins.length > 0}
    <label>Skin
      <select value={chosen ?? skins[0]} onchange={(e) => (chosen = e.currentTarget.value)}>
        {#each skins as name (name)}<option value={name}>{name}</option>{/each}
      </select>
    </label>
  {/if}
  <button onclick={open} disabled={session.busy || reason !== null} title={reason ?? 'Open this model in Blender with Tyrant\'s add-on'}>Open in Blender</button>
  <details>
    <summary>Options</summary>
    <label><input type="checkbox" bind:checked={fresh} /> Start fresh</label>
    <label><input type="checkbox" bind:checked={lods} /> Include far LODs</label>
    <label>Sex
      <select bind:value={sex}>
        <option value="male">Male</option>
        <option value="female">Female</option>
      </select>
    </label>
  </details>
</div>

<style>
  .open-in-blender { display: inline-flex; flex-wrap: wrap; gap: 8px; align-items: center; }
  details { display: inline-block; }
  details label { display: block; white-space: nowrap; }
</style>
