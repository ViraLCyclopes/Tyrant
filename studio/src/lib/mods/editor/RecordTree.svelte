<script lang="ts" module>
  import type { ModAnimalFieldDto } from '$lib/rpc/types.gen';

  export type RecordNode = { name: string; path: string; field?: ModAnimalFieldDto; children: RecordNode[]; count: number };

  /** Folds the record's slash paths (JSON-pointer escaped) into an outline; a folder's own row (array, link) stays on the folder. */
  export function buildRecordTree(fields: ModAnimalFieldDto[]): RecordNode {
    const root: RecordNode = { name: '', path: '', children: [], count: 0 };
    const index = new Map<string, RecordNode>([['', root]]);
    for (const field of fields) {
      let node = root;
      let path = '';
      for (const part of field.path.split('/').slice(1)) {
        path += '/' + part;
        let child = index.get(path);
        if (!child) {
          child = { name: part.replaceAll('~1', '/').replaceAll('~0', '~'), path, children: [], count: 0 };
          index.set(path, child);
          node.children.push(child);
        }
        node = child;
      }
      node.field = field;
    }
    const count = (node: RecordNode): number =>
      (node.count = node.children.length ? node.children.reduce((sum, child) => sum + count(child), 0) : 1);
    count(root);
    return root;
  }
</script>

<script lang="ts">
  import { SvelteSet } from 'svelte/reactivity';

  let { fields, values = $bindable() }: { fields: ModAnimalFieldDto[]; values: Record<string, string> } = $props();

  const MAX_MATCHES = 200;
  const GENERAL = ' general';
  let search = $state('');
  let onlyChanged = $state(false);
  const open = new SvelteSet<string>([GENERAL]);

  const isChanged = (f: ModAnimalFieldDto) => f.editable && (values[f.path] ?? f.value) !== f.templateValue;
  const filtering = $derived(onlyChanged || search.trim() !== '');
  const matches = $derived(
    filtering ? fields.filter((f) => (!onlyChanged || isChanged(f)) && f.path.toLowerCase().includes(search.trim().toLowerCase())) : [],
  );
  // Searching shows matches with their folders open; browsing builds the whole outline once and draws only open folders.
  const tree = $derived(buildRecordTree(filtering ? matches.slice(0, MAX_MATCHES) : fields));
  const general = $derived(tree.children.filter((n) => !n.children.length));
  const folders = $derived(tree.children.filter((n) => n.children.length));
  const changedIn = $derived.by(() => {
    const counts = new Map<string, number>();
    for (const f of fields)
      if (isChanged(f))
        for (let i = f.path.indexOf('/', 1); i > 0; i = f.path.indexOf('/', i + 1)) counts.set(f.path.slice(0, i), (counts.get(f.path.slice(0, i)) ?? 0) + 1);
    return counts;
  });

  const label = (name: string) => (/^\d+$/.test(name) ? '#' + name : name);
  function summary(node: RecordNode): string {
    const own = node.field;
    if (own?.kind === 'array') return ` · ${own.value} items`;
    if (own?.kind === 'reference') {
      try {
        return ` · link: ${(JSON.parse(own.value) as { name?: string }).name ?? own.value}`;
      } catch {
        return ` · link`;
      }
    }
    return '';
  }
  function toggle(key: string) {
    if (open.has(key)) open.delete(key);
    else open.add(key);
  }
</script>

<label>Search fields <input bind:value={search} placeholder="hitPoints, needs, diet, growth…" /></label>
<label class="check"><input type="checkbox" bind:checked={onlyChanged} /> Only changed from the template</label>
<p class="hint">
  {#if !filtering}{fields.length} fields. Open a section to see its fields.
  {:else if matches.length > MAX_MATCHES}Showing {MAX_MATCHES} of {matches.length} matches; narrow the search to see the rest.
  {:else}{matches.length} {matches.length === 1 ? 'match' : 'matches'}{/if}
</p>

{#snippet heading(name: string, node: RecordNode | null, count: number, changed: number)}
  <span class="caret" aria-hidden="true">{filtering || open.has(node?.path ?? GENERAL) ? '▾' : '▸'}</span>
  <span class="name">{name}</span>
  <span class="hint">{count} {count === 1 ? 'field' : 'fields'}{node ? summary(node) : ''}{changed ? ` · ${changed} changed` : ''}</span>
{/snippet}

{#snippet leaf(field: ModAnimalFieldDto, name: string)}
  <label class="field" class:changed={isChanged(field)}><span class="path" title={field.path}>{label(name)}</span>
    {#if field.editable}
      {#if field.kind === 'boolean'}
        <select bind:value={values[field.path]} aria-label={field.path}><option value="true">true</option><option value="false">false</option></select>
      {:else}<input bind:value={values[field.path]} aria-label={field.path} />{/if}
      <span class="hint">Template: {field.templateValue}</span>
    {:else}<span class="hint reference">{field.kind}: {field.value}</span>{/if}
  </label>
{/snippet}

{#snippet branch(nodes: RecordNode[])}
  {#each nodes as node (node.path)}
    {#if node.children.length}
      <div class="folder">
        {#if filtering}
          <div class="toggle">{@render heading(label(node.name), node, node.count, changedIn.get(node.path) ?? 0)}</div>
        {:else}
          <button type="button" class="toggle" aria-expanded={open.has(node.path)} onclick={() => toggle(node.path)}>
            {@render heading(label(node.name), node, node.count, changedIn.get(node.path) ?? 0)}
          </button>
        {/if}
        {#if filtering || open.has(node.path)}<div class="children">{@render branch(node.children)}</div>{/if}
      </div>
    {:else if node.field}
      {@render leaf(node.field, node.name)}
    {/if}
  {/each}
{/snippet}

<div class="record">
  {#if filtering}
    {@render branch(tree.children)}
  {:else}
    {#if general.length}
      <div class="folder">
        <button type="button" class="toggle" aria-expanded={open.has(GENERAL)} onclick={() => toggle(GENERAL)}>
          {@render heading('General', null, general.length, general.filter((n) => n.field && isChanged(n.field)).length)}
        </button>
        {#if open.has(GENERAL)}<div class="children">{@render branch(general)}</div>{/if}
      </div>
    {/if}
    {@render branch(folders)}
  {/if}
</div>

<style>
  label { display: grid; gap: 4px; color: var(--muted); }
  label.check { display: flex; gap: 8px; align-items: center; }
  input:not([type='checkbox']), select { font: inherit; background: var(--bg); color: var(--text); border: 1px solid var(--border); border-radius: 6px; padding: 7px 9px; width: 100%; box-sizing: border-box; }
  p { margin: 0; }
  .hint { font-size: 12px; }
  .record { display: grid; gap: 4px; margin: 12px 0; }
  .folder { display: grid; gap: 4px; }
  .toggle { display: flex; gap: 8px; align-items: baseline; text-align: left; background: none; border: none; padding: 4px 0; color: var(--text); font: inherit; cursor: pointer; }
  div.toggle { cursor: default; }
  .caret { width: 1em; color: var(--muted); }
  .name { font-family: monospace; }
  .children { display: grid; gap: 10px; padding-left: 18px; border-left: 1px solid var(--border); margin-left: 5px; }
  .field.changed .path { color: var(--accent); }
  .path { font-family: monospace; overflow-wrap: anywhere; color: var(--text); }
  .reference { overflow-wrap: anywhere; white-space: pre-wrap; }
</style>
