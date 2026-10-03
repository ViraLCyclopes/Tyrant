<script lang="ts">
  import JobBar from '$lib/components/JobBar.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { LogRecord } from './log';

  let { status, onStatus, onStale }: { status: LogRecord | null; onStatus: () => void; onStale: () => void } = $props();
  const session = getSession();
</script>

<footer class="status">
  {#if status}
    <!-- The shown tab's latest message, so an action always answers; the log has the rest. -->
    <button class="message {status.level}" title="Show the log" onclick={onStatus}>{status.message}</button>
  {:else}
    <span class="workspace path" title={session.workspace?.dir ?? ''}>{session.workspace?.dir ?? 'No workspace open'}</span>
  {/if}
  {#if session.workspace?.stale}
    <button class="badge warn" onclick={onStale}>Game updated: outputs are stale</button>
  {/if}
  <JobBar />
</footer>

<style>
  .status { display: flex; align-items: center; gap: 12px; padding: 4px 12px; border-top: 1px solid var(--border); background: var(--panel); min-height: 32px; font-size: 13px; }
  .workspace { color: var(--muted); flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .message { flex: 1; min-width: 0; text-align: left; border: none; background: none; padding: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .message.warn { color: var(--warn-text); }
  .message.error { color: var(--error-text); }
  .badge { border: none; cursor: pointer; }
</style>
