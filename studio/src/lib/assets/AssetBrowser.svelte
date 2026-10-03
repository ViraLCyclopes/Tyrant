<script lang="ts">
  import { onMount } from 'svelte';
  import { debounce } from '$lib/debounce';
  import type { AssetCount, AssetListResult, AssetsSummary } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import AssetDetail from './AssetDetail.svelte';

  const PAGE_SIZE = 200;
  const session = getSession();
  const tab = getTab();

  let summary = $state<AssetsSummary | null>(null);
  let bundles = $state<Record<string, AssetCount[]>>({});
  let expanded = $state<string | null>(null);
  let group = $state<string | null>(null);
  let bundle = $state<string | null>(null);
  let type = $state('');
  let filter = $state('');
  let page = $state(0);
  let result = $state<AssetListResult | null>(null);
  let checked = $state<string[]>([]);
  let current = $state<string | null>(null);
  let sequence = 0;

  const pages = $derived(result ? Math.max(1, Math.ceil(result.total / result.pageSize)) : 1);
  const lastPart = (name: string) => name.split('/').at(-1) ?? name;

  onMount(async () => {
    summary = await tab.quietly(() => session.rpc.call('assets.summary'));
    await query();
  });

  async function query() {
    const mine = ++sequence;
    const r = await tab.quietly(() =>
      session.rpc.call('assets.list', { filter: filter || null, type: type || null, group, bundle, page, pageSize: PAGE_SIZE }),
    );
    if (mine === sequence && r) result = r; // ignore answers to superseded queries
  }

  const queryLater = debounce(() => {
    page = 0;
    void query();
  }, 250);

  async function toggleGroup(name: string) {
    expanded = expanded === name ? null : name;
    if (expanded === name && !bundles[name]) {
      const r = await tab.quietly(() => session.rpc.call('assets.bundles', { group: name }));
      if (r) bundles[name] = r.bundles;
    }
  }

  function pick(nextGroup: string | null, nextBundle: string | null) {
    group = nextGroup;
    bundle = nextBundle;
    page = 0;
    void query();
  }

  function toggleChecked(ref: string) {
    checked = checked.includes(ref) ? checked.filter((r) => r !== ref) : [...checked, ref];
  }

  function goTo(next: number) {
    page = next;
    void query();
  }

  async function exportChecked() {
    const r = await session.runJob('assets.export', { refs: checked }, 'Export assets', tab);
    if (!r) return;
    const failures = r.failed === 0 ? '' : `, ${r.failed} failed (first: ${r.failures[0]?.name}: ${r.failures[0]?.error})`;
    const notes = r.notes?.length ? ` ${r.notes.join(' ')}` : '';
    tab.info(`Exported ${r.exported} assets${failures}. Report: ${r.reportPath}${notes}`);
  }
</script>

<div class="assets">
  <nav class="tree" aria-label="Asset groups">
    <button class="node" class:active={!group && !bundle} onclick={() => pick(null, null)}>
      All assets {#if summary}<span class="count">{summary.assets.toLocaleString('en-US')}</span>{/if}
    </button>
    {#each summary?.groups ?? [] as g (g.name)}
      <div class="group">
        <button class="expander" aria-expanded={expanded === g.name} aria-label="Show bundles in {g.name}" onclick={() => toggleGroup(g.name)}>
          {expanded === g.name ? '▾' : '▸'}
        </button>
        <button class="node" class:active={group === g.name && !bundle} title={g.name} onclick={() => pick(g.name, null)}>
          {lastPart(g.name)} <span class="count">{g.count}</span>
        </button>
      </div>
      {#if expanded === g.name}
        {#each bundles[g.name] ?? [] as b (b.name)}
          <button class="node bundle" class:active={bundle === b.name} title={b.name} onclick={() => pick(g.name, b.name)}>
            {lastPart(b.name)} <span class="count">{b.count}</span>
          </button>
        {/each}
      {/if}
    {/each}
  </nav>

  <section class="list">
    <div class="toolbar">
      <input type="search" placeholder="Search names, paths, GUIDs…" aria-label="Search assets" bind:value={filter} oninput={queryLater} />
      <label>
        Type
        <select
          value={type}
          aria-label="Type"
          onchange={(e) => {
            type = e.currentTarget.value;
            page = 0;
            void query();
          }}
        >
          <option value="">All types</option>
          {#each summary?.types ?? [] as t (t.name)}<option value={t.name}>{t.name} ({t.count})</option>{/each}
        </select>
      </label>
      <button class="primary" disabled={checked.length === 0 || session.busy} onclick={exportChecked}>Export selected ({checked.length})</button>
    </div>
    {#if summary?.newBundles}
      <p class="warn">{summary.newBundles} bundles were downloaded since the last index (DLC?). Run <strong>Index assets</strong> on the Workspace tab to include them.</p>
    {/if}
    {#if summary?.stale}
      <p class="warn">The asset index is from an older game build. Run <strong>Index assets</strong> on the Workspace tab again.</p>
    {/if}
    {#if result}
      <div class="table-wrap">
        <table class="grid">
          <thead>
            <tr><th><span class="visually-hidden">Select</span></th><th>Name</th><th>Type</th><th>Bundle</th></tr>
          </thead>
          <tbody>
            {#each result.rows as row (row.ref)}
              <tr class:selected={current === row.ref}>
                <td><input type="checkbox" aria-label="Select {row.name}" checked={checked.includes(row.ref)} onchange={() => toggleChecked(row.ref)} /></td>
                <td><button class="link" onclick={() => (current = row.ref)}>{row.name || '(unnamed)'}</button></td>
                <td>{row.type}</td>
                <td title={row.bundle}>{lastPart(row.bundle)}</td>
              </tr>
            {:else}
              <tr><td colspan="4" class="hint">No assets match.</td></tr>
            {/each}
          </tbody>
        </table>
      </div>
      <div class="pager">
        <button disabled={page === 0} onclick={() => goTo(page - 1)}>Previous</button>
        <span>Page {page + 1} of {pages} · {result.total.toLocaleString('en-US')} assets</span>
        <button disabled={page + 1 >= pages} onclick={() => goTo(page + 1)}>Next</button>
      </div>
    {/if}
  </section>

  <section class="detail" aria-label="Asset details">
    {#if current}
      <AssetDetail ref={current} onOpen={(ref) => (current = ref)} />
    {:else}
      <p class="empty">Pick an asset to see its keys, preview and fields.</p>
    {/if}
  </section>
</div>

<style>
  .assets { display: grid; grid-template-columns: 230px minmax(320px, 1fr) minmax(360px, 1.2fr); gap: 12px; height: calc(100vh - 170px); }
  .assets > * { overflow: auto; min-height: 0; }
  .tree, .detail { background: var(--panel); border: 1px solid var(--border); border-radius: var(--radius); padding: 8px; }
  .group { display: flex; align-items: center; }
  .node { display: block; width: 100%; text-align: left; background: none; border: none; padding: 4px 6px; border-radius: 4px; }
  .node.active { background: var(--accent); color: var(--accent-text); }
  .bundle { padding-left: 28px; font-size: 13px; }
  .expander { background: none; border: none; padding: 2px 4px; width: 22px; }
  .count { color: var(--muted); font-size: 12px; }
  .node.active .count { color: inherit; }
  .table-wrap { max-height: calc(100vh - 270px); }
</style>
