<script lang="ts" module>
  /** One entry of a ⋯ menu. */
  export interface ItemAction {
    label: string;
    run: () => void;
    disabled?: boolean;
  }
</script>

<script lang="ts">
  let { label, items }: { label: string; items: () => ItemAction[] } = $props();
  let open = $state(false);
  let shown = $state<ItemAction[]>([]);
  let root = $state<HTMLElement>();

  function toggle() {
    shown = items();
    open = !open;
  }

  function pick(item: ItemAction) {
    open = false;
    item.run();
  }

  function onWindowClick(e: MouseEvent) {
    if (open && root && !root.contains(e.target as Node)) open = false;
  }

  function onKey(e: KeyboardEvent) {
    if (open && e.key === 'Escape') {
      open = false;
      e.stopPropagation();
    }
  }
</script>

<svelte:window onclick={onWindowClick} onkeydown={onKey} />

<span class="menu" bind:this={root}>
  <button class="more" aria-label={label} aria-haspopup="menu" aria-expanded={open} onclick={toggle}>⋯</button>
  {#if open}
    <div class="items" role="menu" aria-label={label}>
      {#each shown as item (item.label)}
        <button role="menuitem" disabled={item.disabled} onclick={() => pick(item)}>{item.label}</button>
      {/each}
    </div>
  {/if}
</span>

<style>
  .menu { position: relative; display: inline-flex; }
  .more { border: none; background: none; padding: 2px 6px; line-height: 1; opacity: 0; }
  :global(.row:hover) .more, :global(.row:focus-within) .more, .more[aria-expanded='true'], .more:focus-visible { opacity: 1; }
  .items { position: absolute; right: 0; top: 100%; z-index: 40; min-width: 200px; display: grid; background: var(--panel); border: 1px solid var(--border); border-radius: var(--radius); padding: 4px; box-shadow: 0 4px 16px #0006; }
  .items button { text-align: left; border: none; background: none; padding: 5px 8px; border-radius: 5px; white-space: nowrap; }
  .items button:hover:not(:disabled) { background: var(--panel-2); }
</style>
