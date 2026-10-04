<script lang="ts">
  import type { ModReplacementDto } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { ModDoc } from './modDoc.svelte';
  import Thumb from './Thumb.svelte';

  let { doc, replacement, onRemoved }: { doc: ModDoc; replacement: ModReplacementDto; onRemoved: () => void } = $props();
  const session = getSession();
  const tab = getTab();

  async function replaceFile() {
    const png = await session.platform.openFile(`Choose the new PNG for ${replacement.texture}`, ['png']);
    if (!png) return;
    const r = await tab.safely(() => session.rpc.call('mods.replace', { id: doc.id, texture: replacement.texture, png }));
    if (r) {
      tab.info(`${replacement.texture} now uses ${png}.`);
      await doc.reload();
    }
  }

  async function remove() {
    if (!(await session.platform.confirm(`Stop replacing ${replacement.texture}? Its PNG stays in the mod's textures folder.`, 'Remove replacement'))) return;
    if (await doc.edit('mods.removeReplacement', { texture: replacement.texture })) onRemoved();
  }

  function folder(): string {
    return `${doc.detail!.dir}\\${replacement.file.replaceAll('/', '\\')}`;
  }
</script>

<section class="card replacement">
  <h2>{replacement.texture}</h2>
  {#if replacement.key}<p class="hint">Addressables path: <code>{replacement.key}</code></p>{/if}
  {#if replacement.guid}<p class="hint">GUID: <code>{replacement.guid}</code></p>{/if}
  <div class="row">
    <Thumb modId={doc.id} file={replacement.file} size={160} label={replacement.texture} version={doc.detail?.revision} />
    <div class="buttons">
      <p class="path">{replacement.file}</p>
      <button onclick={replaceFile} title="Overwrites the PNG in the mod; this cannot be undone">Replace file…</button>
      <button class="ghost" onclick={() => session.platform.reveal(folder())}>Open folder</button>
      <button onclick={remove}>Remove</button>
    </div>
  </div>
  <p class="hint">To add a replacement, open the texture in an Assets tab and use <strong>Replace in a mod…</strong>.</p>
</section>

<style>
  .replacement { display: grid; gap: 10px; }
  .row { display: flex; gap: 16px; align-items: flex-start; flex-wrap: wrap; }
  .buttons { display: grid; gap: 6px; justify-items: start; }
</style>
