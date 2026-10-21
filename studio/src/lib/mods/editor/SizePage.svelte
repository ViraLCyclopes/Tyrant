<script lang="ts">
  import { getSession } from '$lib/stores/session.svelte';
  import type { ModDoc } from './modDoc.svelte';
  import SizeCard from './SizeCard.svelte';
  import { sizeTargets } from './sizes';

  let { doc, target, onRemoved = () => {} }: { doc: ModDoc; target: string; onRemoved?: () => void } = $props();
  const session = getSession();

  const hasFile = $derived(!!doc.detail && sizeTargets(doc.detail).some((t) => t.toLowerCase() === target.toLowerCase()));

  async function removeAll() {
    if (!(await session.platform.confirm(`Remove all size changes of ${target}?`, 'Sizes'))) return;
    if (await doc.edit('mods.setSizes', { target, adult: null, baby: null, male: null, female: null })) onRemoved();
  }
</script>

<header class="head">
  <h2>Sizes: {target}</h2>
  <p class="hint">How big this animal is. Your values replace the game's when the animal spawns; the game's files are not changed.</p>
  <div><button disabled={!hasFile} onclick={removeAll}>Remove all…</button></div>
</header>
<SizeCard {doc} {target} />

<style>
  .head { display: grid; gap: 6px; }
  .head h2, .head p { margin: 0; }
</style>
