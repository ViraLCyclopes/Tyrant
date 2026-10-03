<script lang="ts">
  import JobBar from '$lib/components/JobBar.svelte';
  import { getSession } from '$lib/stores/session.svelte';

  let { onStale }: { onStale: () => void } = $props();
  const session = getSession();
</script>

<footer class="status">
  <span class="workspace path" title={session.workspace?.dir ?? ''}>{session.workspace?.dir ?? 'No workspace open'}</span>
  {#if session.workspace?.stale}
    <button class="badge warn" onclick={onStale}>Game updated: outputs are stale</button>
  {/if}
  <JobBar />
</footer>

<style>
  .status { display: flex; align-items: center; gap: 12px; padding: 4px 12px; border-top: 1px solid var(--border); background: var(--panel); min-height: 32px; font-size: 13px; }
  .workspace { color: var(--muted); flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .badge { border: none; cursor: pointer; }
</style>
