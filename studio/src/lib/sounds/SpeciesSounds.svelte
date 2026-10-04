<script lang="ts">
  import { onMount } from 'svelte';
  import type { SoundDto, SoundsForSpeciesResult } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { AddedSound } from './audio';
  import ReplaceSoundInMod from './ReplaceSoundInMod.svelte';

  /** One species' sounds (its own and the ones it shares), by moment, each with Replace…. */
  let { speciesKey, displayName, modId, onAdded }: { speciesKey: string; displayName: string; modId?: string; onAdded?: (added: AddedSound) => void } =
    $props();

  const session = getSession();
  const tab = getTab();
  let result = $state.raw<SoundsForSpeciesResult | null>(null);
  const groups = $derived.by(() => {
    const byGroup = new Map<string, SoundDto[]>();
    for (const s of result?.sounds ?? []) byGroup.set(s.group, [...(byGroup.get(s.group) ?? []), s]);
    return [...byGroup.entries()];
  });

  onMount(async () => {
    result = await tab.quietly(() => session.rpc.call('sounds.forSpecies', { species: speciesKey }));
  });
</script>

<section class="sounds" aria-label="{displayName} sounds">
  <h3>{displayName} sounds</h3>
  {#if !result}
    <p class="hint">Loading the sounds…</p>
  {:else if !result.speciesId || !result.sounds.length}
    <p class="hint">The data dump has no sounds for {displayName}.</p>
  {:else}
    <p class="hint">
      Shared sounds play for every species that uses them; Replace… can change one for {result.speciesId} only. Babies play the new sound
      higher, as they do the game's.
    </p>
    {#each groups as [group, sounds] (group)}
      <details open={groups.length <= 3}>
        <summary>{group} ({sounds.length})</summary>
        <ul>
          {#each sounds as sound (sound.event)}
            <li>
              <span class="name" title={sound.event}>{sound.name}</span>
              {#if sound.species.length > 1}<span class="shared">shared with {sound.species.length} species</span>{/if}
              <ReplaceSoundInMod {sound} speciesId={result.speciesId} {modId} {onAdded} />
            </li>
          {/each}
        </ul>
      </details>
    {/each}
  {/if}
</section>

<style>
  .sounds { margin-top: 12px; }
  h3 { margin: 0 0 6px; }
  ul { list-style: none; margin: 4px 0 8px; padding: 0 0 0 12px; }
  li { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; padding: 2px 0; }
  .name { min-width: 180px; }
  .shared { color: var(--muted); font-size: 12px; }
  summary { cursor: pointer; padding: 3px 0; }
</style>
