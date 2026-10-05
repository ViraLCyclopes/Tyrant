<script lang="ts">
  import { onMount } from 'svelte';
  import type { IkChainRow } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';

  /** A species' IK chains in the game (FABRIK), as Open in Blender builds them into IK controls. */
  let { speciesKey, displayName }: { speciesKey: string; displayName: string } = $props();
  const session = getSession();
  const tab = getTab();
  let chains = $state<IkChainRow[] | null>(null);
  let failed = $state(false);

  onMount(async () => {
    const r = await tab.safely(() => session.rpc.call('species.ik', { key: speciesKey }));
    if (r) chains = r.chains;
    else failed = true;
  });
</script>

<section class="ik-chains" aria-label="IK chains of {displayName}">
  <h3>IK chains: {displayName}</h3>
  {#if failed}
    <p class="hint">The IK chains could not be read (see the log).</p>
  {:else if chains === null}
    <p class="hint">Reading the game's IK chains…</p>
  {:else if chains.length === 0}
    <p class="hint">{displayName} has no IK chains in the game.</p>
  {:else}
    <ul>
      {#each chains as chain (chain.name)}
        <li><strong>{chain.name}</strong>: {chain.joints.join(' → ')}{chain.poleFrom ? `, pole from ${chain.poleFrom}` : ''}</li>
      {/each}
    </ul>
    <p class="hint">Open in Blender builds these as IK controls (Options → IK controls). Blender's IK is close to the game's, not identical.</p>
  {/if}
</section>

<style>
  .ik-chains { margin-top: 12px; }
  ul { margin: 4px 0; padding-left: 18px; }
</style>
