<script lang="ts">
  import type { ModTexturesResult } from '$lib/rpc/types.gen';
  import type { ModDoc } from './modDoc.svelte';
  import { describe } from './textures';

  let { doc, file, facts }: { doc: ModDoc; file: string; facts: ModTexturesResult | null } = $props();
  const row = $derived(facts?.textures.find((t) => t.file.toLowerCase() === file.toLowerCase()) ?? null);
  const modDefault = $derived(facts?.compression === 'bc7' ? 'BC7' : 'Match the game');

  function choose(value: string) {
    void doc.edit('mods.setCompression', { file, mode: value === '' ? null : value });
  }
</script>

{#if row}
  <div class="compression">
    <select aria-label="Compression of {file}" value={row.override ? row.mode : ''} onchange={(e) => choose(e.currentTarget.value)}>
      <option value="">Mod default ({modDefault})</option>
      <option value="auto">Match the game</option>
      <option value="bc7">BC7 (best quality)</option>
    </select>
    <span class="hint">{describe(row)}</span>
  </div>
{/if}

<style>
  .compression { display: grid; gap: 2px; justify-items: start; }
  select { font-size: 12px; padding: 2px 4px; }
</style>
