<script lang="ts">
  import { tick } from 'svelte';
  import { panelSize, type LogRecord } from './log';

  let {
    records,
    position,
    size,
    onResize,
    onClear,
    onCopy,
    onReveal,
    onDock,
  }: {
    records: LogRecord[];
    position: 'bottom' | 'side';
    size: number;
    onResize: (size: number) => void;
    onClear: () => void;
    onCopy: () => void;
    onReveal: (() => void) | null;
    onDock: () => void;
  } = $props();

  let level = $state<'all' | 'problems'>('all');
  let list = $state<HTMLElement>();
  const shown = $derived(level === 'all' ? records : records.filter((r) => r.level !== 'info'));

  function time(ms: number): string {
    return new Date(ms).toLocaleTimeString('en-GB', { hour12: false });
  }

  // Follow new records while the user is at the bottom; leave them where they are when they scrolled up to read.
  let atBottom = true;
  $effect.pre(() => {
    void shown.length;
    if (list) atBottom = list.scrollHeight - list.scrollTop - list.clientHeight < 24;
    void tick().then(() => {
      if (list && atBottom) list.scrollTop = list.scrollHeight;
    });
  });

  function startResize(e: PointerEvent) {
    e.preventDefault();
    const start = position === 'bottom' ? e.clientY : e.clientX;
    const from = size;
    const viewport = position === 'bottom' ? window.innerHeight : window.innerWidth;
    const move = (m: PointerEvent) => onResize(panelSize({ from, start, now: position === 'bottom' ? m.clientY : m.clientX, position, viewport }));
    const up = () => {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
  }
</script>

<section class="log {position}" style={position === 'bottom' ? `height: ${size}px` : `width: ${size}px`} aria-label="Log">
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="grip" onpointerdown={startResize} title="Drag to resize"></div>
  <header>
    <strong>Log</strong>
    <select aria-label="Show" bind:value={level}>
      <option value="all">All</option>
      <option value="problems">Warnings and errors</option>
    </select>
    <span class="spacer"></span>
    <button class="ghost" onclick={onDock} title="Move the log to the bottom or the side">{position === 'side' ? 'Dock at the bottom' : 'Dock at the side'}</button>
    <button class="ghost" onclick={onCopy}>Copy</button>
    <button class="ghost" onclick={() => onReveal?.()} disabled={!onReveal}>Show studio.log</button>
    <button class="ghost" onclick={onClear}>Clear</button>
  </header>
  <ol bind:this={list}>
    {#each shown as record (record.seq)}
      <li class={record.level}>
        <span class="time">{time(record.time)}</span>
        <span class="message">{record.message}</span>
        {#if record.detail}<span class="detail">{record.detail}</span>{/if}
      </li>
    {:else}
      <li class="empty">Nothing here yet.</li>
    {/each}
  </ol>
</section>

<style>
  .log { position: relative; display: flex; flex-direction: column; background: var(--panel); border-top: 1px solid var(--border); min-height: 0; }
  .log.side { border-top: none; border-left: 1px solid var(--border); flex: none; }
  .log.bottom { flex: none; }
  .grip { position: absolute; z-index: 2; }
  .bottom .grip { left: 0; right: 0; top: -3px; height: 6px; cursor: ns-resize; }
  .side .grip { top: 0; bottom: 0; left: -3px; width: 6px; cursor: ew-resize; }
  header { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; padding: 4px 10px; border-bottom: 1px solid var(--border); }
  .spacer { flex: 1; }
  ol { list-style: none; margin: 0; padding: 4px 10px; overflow: auto; flex: 1; font-family: var(--mono); font-size: 12px; }
  li { display: flex; gap: 10px; padding: 1px 0; }
  .time { color: var(--muted); flex: none; }
  .message { white-space: pre-wrap; word-break: break-word; }
  .detail { color: var(--muted); }
  .warn .message { color: var(--warn-text); }
  .error .message { color: var(--error-text); }
  .empty { color: var(--muted); }
</style>
