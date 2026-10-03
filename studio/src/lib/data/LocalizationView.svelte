<script lang="ts">
  import { onMount } from 'svelte';
  import { debounce } from '$lib/debounce';
  import type { LanguageInfo, LocalizationQueryResult } from '$lib/rpc/types.gen';
  import { getSession } from '$lib/stores/session.svelte';

  const PAGE_SIZE = 100;
  const session = getSession();
  let languages = $state<LanguageInfo[]>([]);
  let chosen = $state<string[]>([]);
  let filter = $state('');
  let page = $state(0);
  let result = $state<LocalizationQueryResult | null>(null);
  let sequence = 0;

  const pages = $derived(result ? Math.max(1, Math.ceil(result.total / result.pageSize)) : 1);
  const nameOf = (code: string) => languages.find((l) => l.code === code)?.name ?? code;

  onMount(async () => {
    const r = await session.quietly(() => session.rpc.call('data.languages'));
    if (!r) return;
    languages = r.languages;
    const english = r.languages.find((l) => l.code.toLowerCase().startsWith('en') || /english/i.test(l.name));
    chosen = (english ? [english] : r.languages.slice(0, 1)).map((l) => l.code);
    await query();
  });

  async function query() {
    const mine = ++sequence;
    const r = await session.quietly(() =>
      session.rpc.call('data.localization', { filter: filter || null, languages: chosen, page, pageSize: PAGE_SIZE }),
    );
    if (mine === sequence && r) result = r; // ignore answers to superseded searches
  }

  const queryLater = debounce(() => {
    page = 0;
    void query();
  }, 250);

  function toggle(code: string) {
    // keep the dump's language order
    chosen = languages.map((l) => l.code).filter((c) => (c === code ? !chosen.includes(c) : chosen.includes(c)));
    page = 0;
    void query();
  }

  function goTo(next: number) {
    page = next;
    void query();
  }
</script>

<div class="toolbar">
  <input type="search" placeholder="Search terms and text…" aria-label="Search localization" bind:value={filter} oninput={queryLater} />
</div>
<fieldset class="languages">
  <legend>Languages</legend>
  {#each languages as l (l.code)}
    <label><input type="checkbox" checked={chosen.includes(l.code)} onchange={() => toggle(l.code)} /> {l.name}</label>
  {/each}
</fieldset>

{#if result}
  <div class="table-wrap">
    <table class="grid">
      <thead>
        <tr>
          <th>Term</th>
          {#each result.languages as code (code)}<th>{nameOf(code)}</th>{/each}
        </tr>
      </thead>
      <tbody>
        {#each result.rows as row (row.term)}
          <tr>
            <td title={row.term}>{row.term}</td>
            {#each row.values as value, i (i)}<td title={value}>{value}</td>{/each}
          </tr>
        {:else}
          <tr><td colspan={result.languages.length + 1} class="hint">No terms match.</td></tr>
        {/each}
      </tbody>
    </table>
  </div>
  <div class="pager">
    <button disabled={page === 0} onclick={() => goTo(page - 1)}>Previous</button>
    <span>Page {page + 1} of {pages} · {result.total.toLocaleString('en-US')} terms</span>
    <button disabled={page + 1 >= pages} onclick={() => goTo(page + 1)}>Next</button>
  </div>
{/if}

<style>
  .languages { display: flex; flex-wrap: wrap; gap: 4px 14px; border: 1px solid var(--border); border-radius: var(--radius); margin: 0 0 12px; padding: 6px 12px; }
</style>
