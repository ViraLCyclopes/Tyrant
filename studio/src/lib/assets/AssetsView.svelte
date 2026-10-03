<script lang="ts">
  import { getSession } from '$lib/stores/session.svelte';
  import AssetBrowser from './AssetBrowser.svelte';
  import SpeciesPanel from './SpeciesPanel.svelte';

  type Tab = 'browse' | 'species';
  const session = getSession();
  const tabs: { id: Tab; label: string }[] = [
    { id: 'browse', label: 'Browse' },
    { id: 'species', label: 'Species' },
  ];
  let tab = $state<Tab>('browse');
</script>

<h1>Assets</h1>

{#if !session.workspace}
  <p class="empty">Open a workspace on the Home tab first.</p>
{:else if !session.workspace.hasAssetIndex}
  <p class="empty">The game's assets are not indexed yet. Click <strong>Index assets</strong> on the Home tab (about 20 seconds).</p>
{:else}
  <div class="tabs" role="tablist" aria-label="Asset views">
    {#each tabs as t (t.id)}
      <button role="tab" aria-selected={tab === t.id} class:active={tab === t.id} onclick={() => (tab = t.id)}>{t.label}</button>
    {/each}
  </div>
  <div role="tabpanel">
    {#if tab === 'browse'}<AssetBrowser />{:else}<SpeciesPanel />{/if}
  </div>
{/if}

<style>
  .tabs { display: flex; gap: 4px; margin-bottom: 14px; border-bottom: 1px solid var(--border); }
  .tabs button { border: none; border-bottom: 2px solid transparent; border-radius: 0; background: none; padding: 8px 14px; }
  .tabs button.active { border-bottom-color: var(--accent); color: var(--accent); font-weight: 600; }
</style>
