<script lang="ts">
  import { onMount } from 'svelte';
  import type { SpeciesRigInfoResult } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';

  /** What a species' skeleton does in game, for rig edits: bones its animations move, bones its growth positions or scales. */
  let { speciesKey, displayName }: { speciesKey: string; displayName: string } = $props();
  const session = getSession();
  const tab = getTab();
  let info = $state<SpeciesRigInfoResult | null>(null);
  let failed = $state(false);

  onMount(async () => {
    const r = await tab.safely(() => session.rpc.call('species.rigInfo', { species: speciesKey }));
    if (r) info = r;
    else failed = true;
  });

  const list = (names: string[]) => (names.length ? names.join(', ') : 'none');
</script>

<section class="rig-info" aria-label="Rig info of {displayName}">
  <h3>Rig edits: {displayName}</h3>
  {#if failed}
    <p class="hint">The rig info could not be read (see the log).</p>
  {:else if info === null}
    <p class="hint">Reading the game's animations and growth…</p>
  {:else}
    <ul>
      <li>Moved by the game's animations: {list(info.clipMoved)}</li>
      <li>Positioned by growth: {list(info.growthMoved)}</li>
      <li>Scaled by growth: {list(info.growthScaled)}</li>
    </ul>
    <p class="hint">
      A rig edit on a bone the animations move changes that motion.
      {info.growthSupported
        ? 'Rig edits on growth bones are applied again after the game\'s growth.'
        : 'Rig edits on growth bones are not supported yet: the game\'s growth puts them back.'}
    </p>
    {#each info.failures as f (f)}<p class="hint">{f}</p>{/each}
  {/if}
</section>

<style>
  .rig-info { margin-top: 12px; }
  ul { margin: 4px 0; padding-left: 18px; }
</style>
