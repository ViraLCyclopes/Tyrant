<script lang="ts">
  import type { ModPrefabFieldDto, ModPrefabFields } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { ModDoc } from './modDoc.svelte';

  let { doc, target, onRemoved = () => {} }: { doc: ModDoc; target: string; onRemoved?: () => void } = $props();
  const session = getSession();
  const tab = getTab();
  let data = $state<ModPrefabFields | null>(null);
  let find = $state('');
  let onlyChanged = $state(false);

  let loads = 0; // the newest request: older replies (another target, or out of order) are dropped
  let failed = $state(false);
  let pending: Record<string, Record<string, number>> = {}; // vector/colour edits not yet reloaded, so a quick second part keeps the first

  // Reload the list when the target or the mod's prefab files change (an edit, undo, or an outside change).
  $effect(() => {
    void doc.detail?.prefabFiles;
    const t = target;
    const mine = ++loads;
    void tab.quietly(() => session.rpc.call('mods.prefabFields', { id: doc.id, target: t })).then((r) => {
      if (mine !== loads) return;
      failed = r === null;
      if (r && r.target === target) {
        pending = {};
        data = r;
      }
    });
  });
  const ready = $derived(data?.target === target ? data : null);

  const changed = $derived(ready?.fields.filter((f) => f.value !== null).length ?? 0);
  const shown = $derived(
    (ready?.fields ?? []).filter(
      (f) => (!onlyChanged || f.value !== null) && (!find.trim() || `${f.object} ${f.component} ${f.field}`.toLowerCase().includes(find.trim().toLowerCase())),
    ),
  );
  const groups = $derived.by(() => {
    const out: { label: string; fields: ModPrefabFieldDto[] }[] = [];
    for (const f of shown) {
      const label = `${f.object === '' ? '(root)' : f.object} ${f.component}`;
      let g = out.find((x) => x.label === label);
      if (!g) out.push((g = { label, fields: [] }));
      g.fields.push(f);
    }
    return out;
  });

  const current = (f: ModPrefabFieldDto) => JSON.parse(f.value ?? f.gameValue) as unknown;
  const address = (f: ModPrefabFieldDto) => ({ target, object: f.object, component: f.component, field: f.field });

  function save(f: ModPrefabFieldDto, value: unknown) {
    void doc.edit('mods.setPrefabField', { ...address(f), value });
  }

  function saveNumber(f: ModPrefabFieldDto, text: string) {
    const n = Number(text);
    if (text.trim() !== '' && Number.isFinite(n)) save(f, n);
  }

  function savePart(f: ModPrefabFieldDto, part: string, text: string) {
    const n = Number(text);
    if (!Number.isFinite(n)) return;
    const key = `${f.object}|${f.component}|${f.field}`;
    const next = { ...(pending[key] ?? (current(f) as Record<string, number>)), [part]: n };
    pending[key] = next;
    save(f, next);
  }

  const parts = (kind: string) =>
    kind === 'vector2' ? ['x', 'y'] : kind === 'vector3' ? ['x', 'y', 'z'] : kind === 'vector4' ? ['x', 'y', 'z', 'w'] : ['r', 'g', 'b', 'a'];
  const enumValue = (f: ModPrefabFieldDto) => {
    const v = current(f);
    if (typeof v !== 'number') return String(v);
    const i = f.enumValues?.indexOf(v) ?? -1;
    return (i >= 0 ? f.enumNames?.[i] : undefined) ?? String(v);
  };

  async function removeAll() {
    if (!(await session.platform.confirm(`Remove all prefab settings of ${target}?`, 'Prefab settings'))) return;
    if (await doc.edit('mods.removePrefabTarget', { target })) onRemoved();
  }
</script>

<header class="head">
  <h2>Prefab settings: {target} <span class="hint">{changed} changed</span></h2>
  <p class="hint">Fields of this animal's prefab components. Your values replace the game's when the animal spawns; the game's files are not changed.</p>
  <div class="row">
    <input type="search" aria-label="Find a field" placeholder="Find a field…" bind:value={find} />
    <label class="row"><input type="checkbox" bind:checked={onlyChanged} /> Show only changed</label>
    <button disabled={changed === 0} onclick={removeAll}>Remove all…</button>
  </div>
</header>

{#if !ready}
  <p class="hint">{failed ? 'The prefab could not be read; the log says why.' : 'Reading the prefab…'}</p>
{:else}
  {#each groups as g (g.label)}
    <fieldset class="card" aria-label={g.label}>
      <legend>{g.label}</legend>
      {#each g.fields as f (f.field)}
        <div class="field" class:changed={f.value !== null} class:locked={!f.editable}>
          <span class="name">{f.field}</span>
          {#if !f.editable}
            <span class="hint">{f.kind === 'reference' ? 'link (read-only)' : `${f.gameValue} items (read-only)`}</span>
          {:else if f.kind === 'number'}
            <input type="number" step="any" aria-label={f.field} value={current(f) as number} onchange={(e) => saveNumber(f, e.currentTarget.value)} />
          {:else if f.kind === 'bool'}
            <input type="checkbox" aria-label={f.field} checked={current(f) as boolean} onchange={(e) => save(f, e.currentTarget.checked)} />
          {:else if f.kind === 'string'}
            <input aria-label={f.field} value={current(f) as string} onchange={(e) => save(f, e.currentTarget.value)} />
          {:else if f.kind === 'enum'}
            <select aria-label={f.field} value={enumValue(f)} onchange={(e) => save(f, e.currentTarget.value)}>
              {#if typeof current(f) === 'number' && (f.enumValues?.indexOf(current(f) as number) ?? -1) < 0}<option value={enumValue(f)}>{enumValue(f)}</option>{/if}
              {#each f.enumNames ?? [] as name (name)}<option value={name}>{name}</option>{/each}
            </select>
          {:else}
            <span class="parts">
              {#each parts(f.kind) as part (part)}
                <label>{part} <input type="number" step="any" aria-label="{f.field} {part}" value={(current(f) as Record<string, number>)[part]} onchange={(e) => savePart(f, part, e.currentTarget.value)} /></label>
              {/each}
            </span>
          {/if}
          {#if f.editable}<span class="hint">game: {f.gameValue}</span>{/if}
          {#if f.value !== null}
            <button class="ghost" aria-label="Reset {f.field}" title="Back to the game's value" onclick={() => doc.edit('mods.resetPrefabField', address(f))}>↺</button>
          {/if}
        </div>
      {/each}
    </fieldset>
  {:else}
    <p class="hint">No fields match.</p>
  {/each}
{/if}

<style>
  .head { display: grid; gap: 6px; }
  .head h2, .head p { margin: 0; }
  .row { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
  fieldset.card { display: grid; gap: 4px; }
  .field { display: grid; grid-template-columns: minmax(180px, 1fr) auto auto auto; gap: 8px; align-items: center; }
  .field.changed .name { font-weight: 600; }
  .field.locked { opacity: 0.6; }
  .name { font-family: var(--mono, monospace); font-size: 12px; overflow: hidden; text-overflow: ellipsis; }
  .parts { display: flex; gap: 6px; }
  .parts input { width: 70px; }
</style>
