<script lang="ts">
  import { onMount } from 'svelte';
  import type { SoundsSearchResult } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { AddedSound } from './audio';
  import ReplaceSoundInMod from './ReplaceSoundInMod.svelte';

  /** Every game sound (buttons, buildings, music, animals), searchable; Replace… replaces one for everyone. */
  let { modId, onAdded }: { modId?: string; onAdded?: (added: AddedSound) => void } = $props();
  const session = getSession();
  const tab = getTab();
  const DEBOUNCE = 250;
  let text = $state('');
  let result = $state.raw<SoundsSearchResult | null>(null);
  let timer: ReturnType<typeof setTimeout> | undefined;
  let generation = 0;

  onMount(() => {
    void search();
    return () => clearTimeout(timer);
  });

  function typed() {
    clearTimeout(timer);
    timer = setTimeout(() => void search(), DEBOUNCE);
  }

  async function search() {
    const mine = ++generation;
    const found = await tab.quietly(() => session.rpc.call('sounds.search', { text: text.trim() }));
    if (found && mine === generation) result = found;
  }
</script>

<section class="all" aria-label="All sounds">
  <h3>All sounds</h3>
  <input type="search" placeholder="Find a sound (click, roar, music…)" aria-label="Find a sound" bind:value={text} oninput={typed} />
  {#if result && !result.hasEventList}
    <p class="hint">Only the animals' sounds are listed. Run data dump on the Workspace tab to list every sound (buttons, buildings, music).</p>
  {/if}
  {#if result}
    <ul>
      {#each result.sounds as sound (sound.event)}
        <li>
          <span class="group">{sound.group}</span>
          <span class="name" title={sound.event}>{sound.name}</span>
          <span class="path">{sound.event}</span>
          <ReplaceSoundInMod {sound} {modId} {onAdded} />
        </li>
      {:else}
        <li class="hint">No sounds match.</li>
      {/each}
    </ul>
    {#if result.sounds.length >= 500}<p class="hint">Showing the first 500; type more of the name to narrow it down.</p>{/if}
  {/if}
</section>

<style>
  .all { margin-top: 12px; display: grid; gap: 8px; }
  h3 { margin: 0; }
  ul { list-style: none; margin: 0; padding: 0; max-height: 480px; overflow: auto; }
  li { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; padding: 3px 0; border-bottom: 1px solid var(--border); }
  .group { color: var(--muted); font-size: 12px; min-width: 110px; }
  .name { min-width: 160px; }
  .path { color: var(--muted); font-family: var(--mono); font-size: 11px; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
</style>
