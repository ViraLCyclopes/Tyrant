<script lang="ts">
  import { onMount } from 'svelte';
  import { debounce } from '$lib/debounce';
  import type { DataQueryResult, DataTypeInfo } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import ColumnPicker from './ColumnPicker.svelte';
  import CompareView from './CompareView.svelte';
  import { saveColumns, savedColumns } from './columns';

  const PAGE_SIZE = 100;
  const session = getSession();
  const tab = getTab();

  let types = $state<DataTypeInfo[]>([]);
  let type = $state('');
  let filter = $state('');
  let sort = $state<string | null>(null);
  let descending = $state(false);
  let page = $state(0);
  let columns = $state<string[] | null>(null);
  let result = $state<DataQueryResult | null>(null);
  let selected = $state<string[]>([]);
  let comparing = $state(false);
  let sequence = 0;

  const pages = $derived(result ? Math.max(1, Math.ceil(result.total / result.pageSize)) : 1);

  onMount(async () => {
    const r = await tab.quietly(() => session.rpc.call('data.types'));
    if (!r) return;
    types = r.types;
    const preferred = r.types.find((t) => t.shortName === 'AnimalData') ?? r.types[0];
    if (preferred) selectType(preferred.fullName);
  });

  function selectType(fullName: string) {
    type = fullName;
    sort = null;
    descending = false;
    page = 0;
    selected = [];
    comparing = false;
    columns = savedColumns(session.store, fullName);
    void query();
  }

  async function query() {
    if (!type) return;
    const mine = ++sequence;
    const r = await tab.quietly(() =>
      session.rpc.call('data.query', { type, filter: filter || null, sort, descending, page, pageSize: PAGE_SIZE, columns }),
    );
    if (mine === sequence && r) result = r; // ignore answers to queries that were superseded
  }

  const queryLater = debounce(() => {
    page = 0;
    void query();
  }, 250);

  function sortBy(column: string) {
    if (sort === column) descending = !descending;
    else {
      sort = column;
      descending = false;
    }
    page = 0;
    void query();
  }

  function goTo(next: number) {
    page = next;
    void query();
  }

  function applyColumns(next: string[]) {
    columns = next;
    saveColumns(session.store, type, next);
    void query();
  }

  function toggle(name: string) {
    selected = selected.includes(name) ? selected.filter((n) => n !== name) : [...selected, name];
  }

  async function exportAs(format: 'csv' | 'json') {
    const shortName = types.find((t) => t.fullName === type)?.shortName ?? 'export';
    const path = await session.platform.saveFile(`Export ${shortName}`, `${shortName}.${format}`, format);
    if (!path) return;
    const r = await tab.safely(() => session.rpc.call('data.export', { type, format, path }));
    if (r) tab.info(`Exported ${r.count.toLocaleString('en-US')} objects to ${r.path}.`);
  }
</script>

<div class="toolbar">
  <label>
    Type
    <select value={type} onchange={(e) => selectType(e.currentTarget.value)}>
      {#each types as t (t.fullName)}<option value={t.fullName}>{t.shortName} ({t.count})</option>{/each}
    </select>
  </label>
  <input type="search" placeholder="Filter rows…" aria-label="Filter rows" bind:value={filter} oninput={queryLater} />
  {#if result}
    <ColumnPicker all={result.allColumns.slice(1)} chosen={result.columns.slice(1)} onApply={applyColumns} />
  {/if}
  <button disabled={selected.length < 2} onclick={() => (comparing = true)}>Compare ({selected.length})</button>
  <button onclick={() => exportAs('csv')}>Export CSV</button>
  <button onclick={() => exportAs('json')}>Export JSON</button>
</div>

{#if comparing}
  <CompareView {type} names={selected} onClose={() => (comparing = false)} />
{:else if result}
  <div class="table-wrap">
    <table class="grid">
      <thead>
        <tr>
          <th><span class="visually-hidden">Select</span></th>
          {#each result.columns as column (column)}
            <th aria-sort={sort === column ? (descending ? 'descending' : 'ascending') : 'none'}>
              <button class="sort" onclick={() => sortBy(column)}>
                {column === '$name' ? 'Name' : column}{sort === column ? (descending ? ' ▼' : ' ▲') : ''}
              </button>
            </th>
          {/each}
        </tr>
      </thead>
      <tbody>
        {#each result.rows as row (row.name)}
          <tr class:selected={selected.includes(row.name)}>
            <td>
              <input type="checkbox" aria-label="Select {row.name}" checked={selected.includes(row.name)} onchange={() => toggle(row.name)} />
            </td>
            {#each row.values as value, i (i)}<td title={value}>{value}</td>{/each}
          </tr>
        {:else}
          <tr><td colspan={result.columns.length + 1} class="hint">No rows match.</td></tr>
        {/each}
      </tbody>
    </table>
  </div>
  <div class="pager">
    <button disabled={page === 0} onclick={() => goTo(page - 1)}>Previous</button>
    <span>Page {page + 1} of {pages} · {result.total.toLocaleString('en-US')} rows</span>
    <button disabled={page + 1 >= pages} onclick={() => goTo(page + 1)}>Next</button>
  </div>
{/if}
