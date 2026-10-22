<script lang="ts">
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';

  /**
   * A mod file's thumbnail; "Base" when the slot uses the base skin's texture (no file of its own). `version` (the mod's
   * revision) changes when a file is replaced under the same name, so the picture is asked for again. `aspect` (width ÷ height)
   * makes a wide box `size` across that shows the whole picture instead of a cropped square.
   */
  let { modId, file, size = 96, label, version = '', aspect }: { modId: string; file: string | null; size?: number; label: string; version?: string; aspect?: number } = $props();
  const session = getSession();
  const tab = getTab();
  let src = $state<string | null>(null);

  $effect(() => {
    const wanted = file;
    void version;
    src = null;
    if (!wanted) return;
    void tab.quietly(() => session.rpc.call('mods.thumbnail', { id: modId, file: wanted, size })).then((r) => {
      if (file === wanted && r?.file) src = session.platform.fileUrl(r.file);
    });
  });
</script>

<div class="thumb" class:whole={aspect !== undefined} style="width: {size}px; height: {aspect ? Math.round(size / aspect) : size}px" title={file ?? 'Uses the base skin'}>
  {#if src}<img {src} alt={label} />{:else}<span>{file ? '…' : 'Base'}</span>{/if}
</div>

<style>
  .thumb { display: grid; place-items: center; border-radius: 6px; background: var(--panel-2); overflow: hidden; color: var(--muted); font-size: 12px; flex: none; }
  img { width: 100%; height: 100%; object-fit: cover; }
  .whole img { object-fit: contain; }
</style>
