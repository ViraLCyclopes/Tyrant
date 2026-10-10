<script lang="ts">
  import type { ModPortraitDto, ModSkinDto } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { ModDoc } from './modDoc.svelte';
  import { PORTRAIT_COLUMNS, PORTRAIT_ROWS, isTwoToOne, slotOf } from './portraits';

  type Target = { skin: ModSkinDto } | { speciesId: string; speciesKey: string };
  let { doc, target }: { doc: ModDoc; target: Target } = $props();
  const session = getSession();
  const tab = getTab();

  /** Row index the game never shows: female on a male-only skin, male on a female-only one. */
  const unusedRow = $derived('skin' in target ? (!target.skin.female ? 1 : !target.skin.male ? 0 : -1) : -1);

  const targetKey = $derived('skin' in target ? `skin:${target.skin.id}` : `species:${target.speciesId}`);
  let loaded = $state<{ key: string; rows: ModPortraitDto[]; failed: boolean } | null>(null);
  const shown = $derived(loaded?.key === targetKey ? loaded.rows : []);
  const loading = $derived(loaded?.key !== targetKey);

  // Follows saves, undo and replaced files; only the latest request may set the rows.
  $effect(() => {
    void doc.detail?.revision;
    void doc.detail?.filesStamp;
    const key = targetKey;
    const params = 'skin' in target ? { id: doc.id, skin: target.skin.id } : { id: doc.id, speciesId: target.speciesId };
    let stale = false;
    void tab.quietly(() => session.rpc.call('mods.portraits', params)).then((r) => {
      if (!stale) loaded = { key, rows: r?.portraits ?? [], failed: !r };
    });
    return () => { stale = true; };
  });

  function set(slot: string, value: { png: string } | null) {
    const edit = { [slot]: value };
    return 'skin' in target
      ? doc.edit('mods.setPortraits', { skin: target.skin.id, set: edit })
      : doc.edit('mods.setSpeciesPortraits', { speciesId: target.speciesId, set: edit }, { undoable: false });
  }

  async function choose(slot: string, label: string) {
    const png = await session.platform.openFile(`Choose a portrait PNG for ${label} (128×64 like the game's)`, ['png']);
    if (png) await set(slot, { png });
  }
</script>

<section class="card">
  <h2>Portraits</h2>
  <p class="hint">The pictures in the animal's info panel, the Nursery and the nest. Empty slots show the game's.</p>
  {#if loading}<p class="hint">Loading…</p>{:else if loaded?.failed}<p class="hint">The current pictures could not be read; you can still choose PNGs.</p>{/if}
  <div class="grid" role="group" aria-label="Portraits">
    <span></span>
    {#each PORTRAIT_COLUMNS as column (column)}<span class="head">{column}</span>{/each}
    {#each PORTRAIT_ROWS as row, r (row)}
      <span class="head">{row}{#if r === unusedRow}<br /><small class="hint">The game never shows this row for this skin.</small>{/if}</span>
      {#each PORTRAIT_COLUMNS as column, c (column)}
        {@const slot = slotOf(r, c)}
        {@const p = shown.find((x) => x.slot === slot)}
        {@const label = `${row}, ${column}`}
        <div class="slot" class:game={!p?.own}>
          {#if p?.file}
            <img src={session.platform.fileUrl(p.file)} alt="{label} ({p.own ? 'this mod' : 'game'})" />
          {:else}
            <span class="empty">Game portrait</span>
          {/if}
          {#if p?.file && !p.own}<span class="tag">game</span>{/if}
          {#if p?.own && p.width > 0 && !isTwoToOne(p.width, p.height)}
            <span class="warn">The game shows portraits at 2:1 (128×64); this one will be stretched.</span>
          {/if}
          <div class="buttons">
            <button aria-label="Choose PNG for {label}" disabled={loading} onclick={() => choose(slot, label)}>Choose PNG…</button>
            <button aria-label="Clear {label}" disabled={loading || !p?.own} onclick={() => set(slot, null)}>Clear</button>
          </div>
        </div>
      {/each}
    {/each}
  </div>
</section>

<style>
  .grid { display: grid; grid-template-columns: auto repeat(4, minmax(136px, 1fr)); gap: 8px; align-items: start; }
  .head { font-size: 12px; color: var(--muted); }
  .slot { display: grid; gap: 4px; position: relative; }
  .slot img, .empty { width: 128px; height: 64px; border-radius: 4px; background: var(--panel-2); object-fit: contain; }
  .empty { display: grid; place-items: center; font-size: 11px; color: var(--muted); }
  .game img { opacity: 0.55; }
  .tag { position: absolute; top: 2px; left: 2px; font-size: 10px; padding: 0 4px; border-radius: 3px; background: var(--panel); color: var(--muted); }
  .warn { font-size: 11px; color: var(--warn-text); }
  .buttons { display: flex; gap: 4px; }
</style>
