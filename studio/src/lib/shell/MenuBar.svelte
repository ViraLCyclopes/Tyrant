<script lang="ts">
  import { isEnabled, itemsOf, type Menu, type MenuItem } from './menu';

  let { menus }: { menus: Menu[] } = $props();
  let open = $state<string | null>(null);
  /** The rows of the open menu, read when it opened (so checks and enabled states are current). */
  let rows = $state<MenuItem[]>([]);
  let bar = $state<HTMLElement>();

  function toggle(menu: Menu) {
    if (open === menu.label) {
      open = null;
      return;
    }
    rows = itemsOf(menu);
    open = menu.label;
  }

  function run(item: MenuItem) {
    if (item.separator || !isEnabled(item)) return;
    open = null;
    item.run();
  }

  function onWindowClick(e: MouseEvent) {
    if (open && bar && !bar.contains(e.target as Node)) open = null;
  }

  function onKey(e: KeyboardEvent) {
    if (e.key === 'Escape' && open) open = null;
  }
</script>

<svelte:window onclick={onWindowClick} onkeydown={onKey} />

<nav class="menubar" aria-label="Menu" bind:this={bar}>
  {#each menus as menu (menu.label)}
    <div class="slot">
      <button aria-haspopup="menu" aria-expanded={open === menu.label} class:open={open === menu.label} onclick={() => toggle(menu)}>{menu.label}</button>
      {#if open === menu.label}
        <div class="dropdown" role="menu" aria-label={menu.label}>
          {#each rows as item, i (i)}
            {#if item.separator}
              <hr />
            {:else}
              <button role="menuitem" disabled={!isEnabled(item)} onclick={() => run(item)}>
                <span class="check" aria-hidden="true">{item.checked?.() ? '✓' : ''}</span>
                <span class="text">{item.label}</span>
                {#if item.shortcut}<span class="shortcut">{item.shortcut}</span>{/if}
              </button>
            {/if}
          {/each}
        </div>
      {/if}
    </div>
  {/each}
</nav>

<style>
  .menubar { display: flex; gap: 2px; padding: 2px 8px; background: var(--bg); border-bottom: 1px solid var(--border); }
  .slot { position: relative; }
  .slot > button { border: none; background: none; padding: 3px 9px; color: var(--muted); }
  .slot > button:hover, .slot > button.open { color: var(--text); background: var(--panel-2); }
  .dropdown { position: absolute; top: 100%; left: 0; z-index: 30; min-width: 230px; max-width: 520px; background: var(--panel); border: 1px solid var(--border); border-radius: 6px; padding: 4px; box-shadow: 0 8px 24px #0008; display: grid; }
  .dropdown button { display: grid; grid-template-columns: 16px 1fr auto; gap: 8px; text-align: left; border: none; background: none; padding: 5px 8px; border-radius: 4px; }
  .dropdown button:hover:not(:disabled) { background: var(--panel-2); }
  .text { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .shortcut { color: var(--muted); font-size: 12px; }
  hr { border: none; border-top: 1px solid var(--border); margin: 4px 2px; }
</style>
