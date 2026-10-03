<script lang="ts">
  import type { ShellState } from './shellState.svelte';

  let { shell }: { shell: ShellState } = $props();

  function onAux(e: MouseEvent, id: string, closable: boolean) {
    if (e.button === 1 && closable) {
      e.preventDefault();
      shell.close(id);
    }
  }
</script>

<div class="strip">
  <div class="tabs" role="tablist" aria-label="Open tabs">
    {#each shell.tabs as record (record.id)}
      {@const title = shell.title(record.id)}
      {@const marker = shell.marker(record.id)}
      <div class="tab" class:active={record.id === shell.activeId}>
        <button
          role="tab"
          class="label"
          aria-selected={record.id === shell.activeId}
          title={title}
          onclick={() => shell.activate(record.id)}
          onauxclick={(e) => onAux(e, record.id, record.closable)}
        >
          <span class="name">{title}</span>
          {#if marker === 'error'}<span class="mark error" aria-label="{title} has errors">✕</span>{/if}
          {#if marker === 'warn'}<span class="mark warn" aria-label="{title} has warnings">⚠</span>{/if}
        </button>
        {#if record.closable}
          <button class="close" aria-label="Close {title}" onclick={() => shell.close(record.id)}>✕</button>
        {/if}
      </div>
    {/each}
  </div>
  <button class="new" aria-label="New Assets tab" title="New Assets tab" onclick={() => shell.openTool('assets')}>+</button>
</div>

<style>
  .strip { display: flex; align-items: flex-end; gap: 4px; background: var(--panel); padding: 6px 8px 0; border-bottom: 1px solid var(--border); min-width: 0; }
  .tabs { display: flex; gap: 2px; overflow-x: auto; min-width: 0; scrollbar-width: thin; }
  .tab { display: flex; align-items: center; border-radius: 6px 6px 0 0; background: var(--panel-2); }
  .tab.active { background: var(--bg); }
  .label { display: flex; align-items: center; gap: 6px; border: none; background: none; border-radius: 6px 6px 0 0; padding: 6px 10px; color: var(--muted); max-width: 220px; }
  .tab.active .label { color: var(--text); }
  .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .mark.warn { color: var(--warn-text); }
  .mark.error { color: var(--error-text); }
  .close { border: none; background: none; padding: 2px 8px 2px 0; color: var(--muted); }
  .close:hover { color: var(--text); }
  .new { border: none; background: none; padding: 4px 10px; font-size: 16px; color: var(--muted); }
</style>
