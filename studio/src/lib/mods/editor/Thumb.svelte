<script lang="ts">
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';

  /** A mod file's thumbnail; "Base" when the slot uses the base skin's texture (no file of its own). */
  let { modId, file, size = 96, label }: { modId: string; file: string | null; size?: number; label: string } = $props();
  const session = getSession();
  const tab = getTab();
  let src = $state<string | null>(null);

  $effect(() => {
    const wanted = file;
    src = null;
    if (!wanted) return;
    void tab.quietly(() => session.rpc.call('mods.thumbnail', { id: modId, file: wanted, size })).then((r) => {
      if (file === wanted && r?.file) src = session.platform.fileUrl(r.file);
    });
  });
</script>

<div class="thumb" style="width: {size}px; height: {size}px" title={file ?? 'Uses the base skin'}>
  {#if src}<img {src} alt={label} />{:else}<span>{file ? '…' : 'Base'}</span>{/if}
</div>

<style>
  .thumb { display: grid; place-items: center; border-radius: 6px; background: var(--panel-2); overflow: hidden; color: var(--muted); font-size: 12px; flex: none; }
  img { width: 100%; height: 100%; object-fit: cover; }
</style>
