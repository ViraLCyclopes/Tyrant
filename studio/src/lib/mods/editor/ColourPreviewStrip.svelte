<script lang="ts">
  import { asRpcError } from '$lib/rpc/client';
  import type { ModSkinDto } from '$lib/rpc/types.gen';
  import { getSession } from '$lib/stores/session.svelte';
  import type { Variant } from './colors';
  import type { ModDoc } from './modDoc.svelte';

  let { doc, skin, colorsJson, variant }: { doc: ModDoc; skin: ModSkinDto; colorsJson: string | null; variant: Variant } = $props();
  const session = getSession();
  let files = $state<string[]>([]);
  let reason = $state<string | null>(null);
  let seed = $state(1);
  let generation = 0;
  const sex = $derived(skin.male?.diffuse || !skin.female ? 'male' : 'female');

  async function draw() {
    const mine = ++generation;
    try {
      const r = await session.rpc.call('mods.colorPreview', { id: doc.id, skin: skin.id, colors: colorsJson, variant, sex, seed, count: 6, size: 256 });
      if (mine !== generation) return; // an older request: a newer one is on its way
      files = r.files;
      reason = null;
    } catch (e) {
      if (mine !== generation) return;
      files = [];
      reason = asRpcError(e).message;
    }
  }

  // Redraw shortly after the colours, the animal type, the seed, the skin or its files (the mod's revision) change.
  $effect(() => {
    void colorsJson;
    void variant;
    void seed;
    void skin.id;
    void sex;
    void doc.detail?.revision;
    const timer = setTimeout(() => void draw(), 250);
    return () => clearTimeout(timer);
  });
</script>

<div class="strip">
  <div class="animals">
    {#each files as file, i (file)}
      <img src={session.platform.fileUrl(file)} alt="Preview animal {i + 1}" />
    {:else}
      <span class="hint">{reason ?? 'Drawing…'}</span>
    {/each}
  </div>
  <button class="ghost" aria-label="Show 6 other animals" title="Show 6 other animals" onclick={() => { seed += 1; void draw(); }}>↻</button>
  <span class="note">Approximate 2D picture of six animals (no lighting); the 3D view above follows the game's shader more closely.</span>
</div>

<style>
  .strip { display: grid; grid-template-columns: 1fr auto; gap: 6px 10px; align-items: center; }
  .animals { display: flex; gap: 6px; overflow-x: auto; min-height: 96px; align-items: center; }
  img { width: 96px; height: 96px; object-fit: cover; border-radius: 6px; background: var(--panel-2); flex: none; }
  .note { grid-column: 1 / -1; font-size: 12px; color: var(--muted); }
</style>
