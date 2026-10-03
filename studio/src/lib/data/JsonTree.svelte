<script lang="ts">
  import JsonTree from './JsonTree.svelte';

  let { value, name = null, depth = 0 }: { value: unknown; name?: string | null; depth?: number } = $props();

  // Only the first level starts open; deeper levels open on click.
  let open = $state((() => depth < 1)());

  const isObject = $derived(value !== null && typeof value === 'object');
  const entries = $derived<[string, unknown][]>(
    !isObject ? [] : Array.isArray(value) ? value.map((v, i) => [String(i), v]) : Object.entries(value as Record<string, unknown>),
  );
  const reference = $derived(
    isObject && !Array.isArray(value) ? ((value as Record<string, unknown>)['$ref'] as { name?: string; type?: string } | undefined) : undefined,
  );

  function scalar(v: unknown): string {
    return typeof v === 'string' ? `"${v}"` : String(v);
  }
</script>

{#if reference}
  <div class="node">
    {#if name !== null}<span class="key">{name}:</span>{/if}
    <span class="ref">→ {reference.name} <span class="type">({reference.type})</span></span>
  </div>
{:else if isObject}
  <div class="node">
    <button class="toggle" aria-expanded={open} onclick={() => (open = !open)}>
      {open ? '▾' : '▸'}
      {#if name !== null}<span class="key">{name}</span>{/if}
      <span class="summary">{Array.isArray(value) ? `[${entries.length}]` : `{${entries.length}}`}</span>
    </button>
    {#if open}
      <div class="children">
        {#each entries as [key, child] (key)}
          <JsonTree value={child} name={key} depth={depth + 1} />
        {/each}
      </div>
    {/if}
  </div>
{:else}
  <div class="node">
    {#if name !== null}<span class="key">{name}:</span>{/if}
    <span class="value">{scalar(value)}</span>
  </div>
{/if}

<style>
  .node { font-family: var(--mono); font-size: 12.5px; line-height: 1.6; }
  .children { padding-left: 18px; border-left: 1px solid var(--border); margin-left: 6px; }
  .toggle { background: none; border: none; padding: 0; font: inherit; }
  .key { color: var(--accent); margin-right: 4px; }
  .summary, .type { color: var(--muted); }
  .ref { color: var(--warn-text); }
</style>
