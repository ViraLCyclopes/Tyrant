<script lang="ts">
  import MaterialEditor from './MaterialEditor.svelte';
  import type { ModDoc } from './modDoc.svelte';

  /** A species' material settings (mod.json's models entry for it): the Material card, for every skin of the species. No 3D view: skin pages preview it. */
  let { doc, species, onRemoved = () => {} }: { doc: ModDoc; species: string; onRemoved?: () => void } = $props();
  const saved = $derived(doc.detail?.speciesMaterials?.some((m) => m.species.toLowerCase() === species.toLowerCase()) ?? false);

  async function remove() {
    if (await doc.edit('mods.setMaterials', { species, materials: null })) onRemoved();
  }
</script>

<section class="card">
  <h2>Species materials: {species}</h2>
  <p class="hint">Every skin of {species} wears these, under its own material settings.</p>
  <MaterialEditor {doc} {species} />
  <div class="row"><button disabled={!saved} onclick={remove}>Remove</button></div>
</section>

<style>
  .card { display: grid; gap: 10px; }
  .card h2, .card p { margin: 0; }
  .row { display: flex; gap: 8px; }
</style>
