<script lang="ts">
  import { onMount } from 'svelte';
  import { debounce } from '$lib/debounce';
  import type { DataTypeInfo } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import JsonTree from './JsonTree.svelte';

  const session = getSession();
  const tab = getTab();
  let types = $state<DataTypeInfo[]>([]);
  let type = $state<string | null>(null);
  let filter = $state('');
  let names = $state<string[]>([]);
  let name = $state<string | null>(null);
  let json = $state<unknown>(null);
  let namesSequence = 0;
  let objectSequence = 0;

  onMount(async () => {
    const r = await tab.quietly(() => session.rpc.call('data.types'));
    if (r) types = r.types;
  });

  async function pickType(fullName: string) {
    type = fullName;
    name = null;
    json = null;
    filter = '';
    await loadNames();
  }

  async function loadNames() {
    const current = type;
    if (!current) return;
    const mine = ++namesSequence;
    const r = await tab.quietly(() => session.rpc.call('data.objects', { type: current, filter: filter || null }));
    if (mine === namesSequence && r) names = r.names; // ignore answers to superseded lookups
  }

  const loadNamesLater = debounce(() => void loadNames(), 250);

  async function pickObject(objectName: string) {
    const current = type;
    if (!current) return;
    name = objectName;
    const mine = ++objectSequence;
    const r = await tab.quietly(() => session.rpc.call('data.object', { type: current, name: objectName }));
    if (mine === objectSequence) json = r?.json ?? null; // a later click wins
  }

  async function copyJson() {
    await session.platform.copy(JSON.stringify(json, null, 2));
    tab.info('JSON copied to the clipboard.');
  }
</script>

<div class="browse">
  <ul class="list" aria-label="Types">
    {#each types as t (t.fullName)}
      <li>
        <button class:active={t.fullName === type} title={t.fullName} onclick={() => pickType(t.fullName)}>
          {t.shortName} <span class="count">{t.count}</span>
        </button>
      </li>
    {/each}
  </ul>
  <div class="objects">
    {#if type}
      <input type="search" placeholder="Filter objects…" aria-label="Filter objects" bind:value={filter} oninput={loadNamesLater} />
      <ul class="list" aria-label="Objects">
        {#each names as n (n)}
          <li><button class:active={n === name} onclick={() => pickObject(n)}>{n}</button></li>
        {/each}
      </ul>
    {:else}
      <p class="empty">Pick a type.</p>
    {/if}
  </div>
  <div class="detail">
    {#if name && json !== null}
      <div class="toolbar">
        <h2>{name}</h2>
        <button onclick={copyJson}>Copy JSON</button>
      </div>
      <JsonTree value={json} />
    {:else}
      <p class="empty">Pick an object to see all of its fields.</p>
    {/if}
  </div>
</div>

<style>
  .browse { display: grid; grid-template-columns: 240px 260px 1fr; gap: 12px; height: calc(100vh - 210px); }
  .browse > * { overflow: auto; background: var(--panel); border: 1px solid var(--border); border-radius: var(--radius); padding: 8px; }
  .list { list-style: none; margin: 0; padding: 0; }
  .list button { width: 100%; text-align: left; background: none; border: none; padding: 4px 8px; border-radius: 4px; }
  .list button.active { background: var(--accent); color: var(--accent-text); }
  .count { color: var(--muted); font-size: 12px; }
  .objects input[type='search'] { width: 100%; margin-bottom: 6px; }
</style>
