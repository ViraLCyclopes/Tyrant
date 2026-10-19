<script lang="ts">
  import MaterialEditor from './MaterialEditor.svelte';
  import type { ModDoc } from './modDoc.svelte';

  /** A vanilla skin's material settings (mod.json's vanillaSkinMaterials entry): the Material card, for that one game skin. No 3D view. */
  let { doc, species, skin, onRemoved = () => {} }: { doc: ModDoc; species: string; skin: string; onRemoved?: () => void } = $props();
  const saved = $derived(
    doc.detail?.vanillaSkinMaterials?.some((m) => m.species.toLowerCase() === species.toLowerCase() && m.skin.toLowerCase() === skin.toLowerCase()) ?? false,
  );

  async function remove() {
    if (await doc.edit('mods.setMaterials', { vanillaSkin: `${species}/${skin}`, materials: null })) onRemoved();
  }
</script>

<section class="card">
  <h2>Vanilla skin materials: {species} / {skin}</h2>
  <p class="hint">Only the game's {skin} skin of {species} wears these, on top of the species materials.</p>
  <MaterialEditor {doc} vanillaSkin={{ species, skin }} />
  <div class="row"><button disabled={!saved} onclick={remove}>Remove</button></div>
</section>

<style>
  .card { display: grid; gap: 10px; }
  .card h2, .card p { margin: 0; }
  .row { display: flex; gap: 8px; }
</style>
