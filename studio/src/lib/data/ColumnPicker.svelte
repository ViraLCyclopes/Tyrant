<script lang="ts">
  let { all, chosen, onApply }: { all: string[]; chosen: string[]; onApply: (columns: string[]) => void } = $props();

  let open = $state(false);
  let search = $state('');
  let picked = $state<string[]>([]);
  const visible = $derived(all.filter((c) => c.toLowerCase().includes(search.toLowerCase())));

  function show() {
    picked = [...chosen];
    search = '';
    open = true;
  }

  function toggle(column: string) {
    picked = picked.includes(column) ? picked.filter((c) => c !== column) : [...picked, column];
  }

  function apply() {
    onApply(all.filter((c) => picked.includes(c))); // keep the table's column order
    open = false;
  }
</script>

<div class="picker">
  <button onclick={show} aria-expanded={open}>Columns ({chosen.length}/{all.length})</button>
  {#if open}
    <div class="popover" role="dialog" aria-label="Choose columns">
      <input type="search" placeholder="Find a column…" aria-label="Find a column" bind:value={search} />
      <div class="list">
        {#each visible as column (column)}
          <label><input type="checkbox" checked={picked.includes(column)} onchange={() => toggle(column)} /> {column}</label>
        {/each}
      </div>
      <div class="row">
        <button onclick={() => (picked = [])}>None</button>
        <button onclick={() => (picked = [...all])}>All</button>
        <button class="primary" onclick={apply}>Apply</button>
        <button onclick={() => (open = false)}>Close</button>
      </div>
    </div>
  {/if}
</div>

<style>
  .picker { position: relative; }
  .popover {
    position: absolute; z-index: 5; top: calc(100% + 4px); left: 0; width: 360px; padding: 10px;
    background: var(--panel); border: 1px solid var(--border); border-radius: var(--radius); box-shadow: 0 6px 24px rgb(0 0 0 / 0.18);
  }
  .popover input[type='search'] { width: 100%; }
  .list { max-height: 320px; overflow: auto; display: grid; gap: 2px; margin-top: 8px; font-size: 13px; }
</style>
