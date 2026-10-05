<script lang="ts">
  import type { ModSkinDto } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import { restoreCutouts } from '../modActions';
  import type { ModDoc } from './modDoc.svelte';
  import { isColourSlot, SLOT_LABELS, slotsFor } from './slots';
  import Thumb from './Thumb.svelte';

  let { doc, skin, sex }: { doc: ModDoc; skin: ModSkinDto; sex: 'male' | 'female' } = $props();
  const session = getSession();
  const tab = getTab();
  const own = $derived(sex === 'male' ? skin.male : skin.female);
  const base = $derived(sex === 'male' ? skin.baseMaleSlots : skin.baseFemaleSlots);
  const slots = $derived(slotsFor(own ?? null, base ?? null));
  const label = $derived(sex === 'male' ? 'Male' : 'Female');

  async function replace(slot: string) {
    const png = await session.platform.openFile(`Choose the ${sex} ${SLOT_LABELS[slot]} PNG`, ['png']);
    if (png) await doc.edit('mods.setSkinFile', { skin: skin.id, sex, slot, png });
  }

  async function restore() {
    if (await restoreCutouts(session, tab, doc.id)) await doc.reload();
  }

  /** The file itself, or the skin's folder when the slot uses the base skin. */
  function pathOf(file: string | null): string {
    const dir = doc.detail!.dir;
    return file ? `${dir}\\${file.replaceAll('/', '\\')}` : `${dir}\\skins\\${skin.id}`;
  }
</script>

<div class="files" role="group" aria-label="{label} files">
  <h3>{label}</h3>
  {#if base && base.length === 0 && !own}
    <p class="hint">The base skin has no {sex} textures.</p>
  {:else}
    <ul>
      {#each slots as slot (slot)}
        {@const file = own?.[slot] ?? null}
        {@const name = `${sex} ${SLOT_LABELS[slot]}`}
        <li data-slot={slot}>
          <Thumb modId={doc.id} {file} label={name} version="{doc.detail?.revision}|{doc.detail?.filesStamp}" />
          <span class="slot">{SLOT_LABELS[slot]}</span>
          <div class="buttons">
            <button aria-label="Replace {name}…" title="Copies your PNG into the skin's folder; this cannot be undone" onclick={() => replace(slot)}>Replace…</button>
            <button aria-label="Use base for {name}" disabled={!file} onclick={() => doc.edit('mods.setSkinFile', { skin: skin.id, sex, slot, png: null })}>Use base</button>
            {#if !file && base?.includes(slot)}
              <button aria-label="Copy the base {name} to edit" title="Copies the base skin's texture into the skin's folder so you can paint over it"
                onclick={() => doc.edit('mods.copyBaseFile', { skin: skin.id, sex, slot })}>Edit a copy</button>
            {/if}
            <button class="ghost" aria-label="Open folder of {name}" onclick={() => session.platform.reveal(pathOf(file))}>Open folder</button>
            {#if isColourSlot(slot) && file}
              <button class="ghost" aria-label="Restore cutouts of {name}" onclick={restore}>Restore cutouts</button>
            {/if}
          </div>
        </li>
      {/each}
    </ul>
  {/if}
</div>

<style>
  ul { list-style: none; padding: 0; margin: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 12px; }
  li { display: grid; gap: 6px; justify-items: start; }
  .slot { font-weight: 600; }
  .buttons { display: flex; flex-wrap: wrap; gap: 4px; }
  .buttons button { padding: 3px 8px; font-size: 12px; }
</style>
