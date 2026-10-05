<script lang="ts">
  import type { ModRigView } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { ModDoc } from './modDoc.svelte';

  /** A species' or a skin's rig edit (made in Blender): its bones, what the game does with them, and Clear rig edit. */
  let { doc, target, skin }: { doc: Pick<ModDoc, 'id' | 'detail' | 'edit'>; target: string; skin: string | null } = $props();
  const session = getSession();
  const tab = getTab();
  let view = $state<ModRigView | null>(null);

  // Read again whenever the mod changes (a send from Blender, an undo).
  $effect(() => {
    void doc.detail?.revision;
    void load();
  });

  async function load() {
    const r = await tab.quietly(() => session.rpc.call('mods.rig', { id: doc.id, target, skin }));
    if (r) view = r;
  }

  function round(v: number) {
    return Math.round(v * 10000) / 10000;
  }

  /** Degrees about X, Y and Z, for reading (the rig edit keeps the quaternion). */
  function degrees([x, y, z, w]: number[]) {
    const rx = Math.atan2(2 * (w * x + y * z), 1 - 2 * (x * x + y * y));
    const ry = Math.asin(Math.max(-1, Math.min(1, 2 * (w * y - z * x))));
    const rz = Math.atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z));
    return [rx, ry, rz].map((r) => round((r * 180) / Math.PI));
  }

  function parts(b: { move: number[]; rotate: number[]; scale: number[] }) {
    const out: string[] = [];
    if (b.move.some((v) => Math.abs(v) > 1e-6)) out.push(`move ${b.move.map(round).join(', ')}`);
    if (Math.abs(b.rotate[3]) < 1 - 1e-7) out.push(`rotate ${degrees(b.rotate).join(', ')}°`);
    if (b.scale.some((v) => Math.abs(v - 1) > 1e-6)) out.push(`scale ${b.scale.map(round).join(', ')}`);
    return out;
  }

  async function clear() {
    if (view?.hasModel && !(await session.platform.confirm(
      'The model was made for the edited skeleton: without the rig edit it will not fit the game\'s skeleton until you send it again from Blender. Clear the rig edit?',
      'Clear rig edit'))) return;
    await doc.edit('mods.clearRig', { target, skin });
  }
</script>

<section class="rig" aria-label="Rig edit">
  <h3>Rig edit</h3>
  {#if view === null}
    <p class="hint">Reading the rig edit…</p>
  {:else if view.bones.length === 0}
    <p class="hint">No rig edit. Make one in Blender: Open in Blender → Tyrant panel → Rig edit (Start, pose the bones, Apply), then Send.</p>
  {:else}
    <ul>
      {#each view.bones as b (b.bone)}
        <li><strong>{b.bone}</strong>{#each parts(b) as p (p)} <span>{p}</span>{/each}</li>
      {/each}
    </ul>
    {#each view.errors as e (e)}<p class="warn">{e}</p>{/each}
    {#each view.warnings as w (w)}<p class="hint">{w}</p>{/each}
    <div class="row"><button onclick={clear}>Clear rig edit</button></div>
    <p class="hint">Change it in Blender: Open in Blender → Tyrant panel → Rig edit.</p>
  {/if}
</section>

<style>
  .rig { margin-top: 12px; }
  ul { margin: 4px 0; padding-left: 18px; }
  li span { margin-left: 8px; }
  .row { display: flex; gap: 8px; margin: 8px 0; }
</style>
