<script lang="ts">
  import { onMount } from 'svelte';
  import type { DataCompareResult } from '$lib/rpc/types.gen';
  import { getSession } from '$lib/stores/session.svelte';

  let { type, names, onClose }: { type: string; names: string[]; onClose: () => void } = $props();

  const session = getSession();
  let onlyDifferences = $state(true);
  let result = $state<DataCompareResult | null>(null);

  async function load() {
    result = await session.safely(() => session.rpc.call('data.compare', { type, names, onlyDifferences }));
  }

  onMount(load);
</script>

<div class="toolbar">
  <h2>Comparing {names.length}</h2>
  <label>
    <input
      type="checkbox"
      checked={onlyDifferences}
      onchange={(e) => {
        onlyDifferences = e.currentTarget.checked;
        void load();
      }}
    />
    Only differences
  </label>
  <button onclick={onClose}>Back to the table</button>
</div>

{#if result}
  <div class="table-wrap">
    <table class="grid">
      <thead>
        <tr>
          <th>Field</th>
          {#each result.names as name (name)}<th>{name}</th>{/each}
        </tr>
      </thead>
      <tbody>
        {#each result.fields as field, i (field)}
          <tr>
            <th scope="row">{field}</th>
            {#each result.values[i] as value, j (j)}<td title={value}>{value}</td>{/each}
          </tr>
        {:else}
          <tr><td colspan={result.names.length + 1} class="hint">No differences.</td></tr>
        {/each}
      </tbody>
    </table>
  </div>
{/if}
