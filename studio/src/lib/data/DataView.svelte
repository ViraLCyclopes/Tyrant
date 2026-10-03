<script lang="ts">
  import { getSession } from '$lib/stores/session.svelte';
  import BrowseView from './BrowseView.svelte';
  import LocalizationView from './LocalizationView.svelte';
  import TableView from './TableView.svelte';

  type Tab = 'table' | 'browse' | 'localization';
  const session = getSession();
  const tabs: { id: Tab; label: string }[] = [
    { id: 'table', label: 'Tables' },
    { id: 'browse', label: 'Browse' },
    { id: 'localization', label: 'Localization' },
  ];
  let tab = $state<Tab>('table');
</script>

<h1>Game data</h1>

{#if !session.workspace}
  <p class="empty">Open a workspace on the Home tab first.</p>
{:else if !session.workspace.hasData}
  <p class="empty">No game data yet. Install the dumper and run a data dump on the Home tab.</p>
{:else}
  <div class="tabs" role="tablist" aria-label="Data views">
    {#each tabs as t (t.id)}
      <button role="tab" aria-selected={tab === t.id} class:active={tab === t.id} onclick={() => (tab = t.id)}>{t.label}</button>
    {/each}
  </div>
  <div role="tabpanel">
    {#if tab === 'table'}
      <TableView />
    {:else if tab === 'browse'}
      <BrowseView />
    {:else}
      <LocalizationView />
    {/if}
  </div>
{/if}

<style>
  .tabs { display: flex; gap: 4px; margin-bottom: 14px; border-bottom: 1px solid var(--border); }
  .tabs button { border: none; border-bottom: 2px solid transparent; border-radius: 0; background: none; padding: 8px 14px; }
  .tabs button.active { border-bottom-color: var(--accent); color: var(--accent); font-weight: 600; }
</style>
