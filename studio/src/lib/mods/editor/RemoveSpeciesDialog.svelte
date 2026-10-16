<script lang="ts">
  import { onMount } from 'svelte';
  import type { SpeciesRemoval } from '$lib/rpc/types.gen';

  /** plan: what mods.speciesRemoval says goes with the species, and which other mods use it. */
  let { plan, onConfirm, onCancel }: { plan: SpeciesRemoval; onConfirm: (deleteFiles: boolean) => void; onCancel: () => void } = $props();
  let deleteFiles = $state(true);
  let dialog = $state<HTMLElement>();

  const count = (n: number, one: string) => `${n} ${one}${n === 1 ? '' : 's'}`;
  const goes = $derived.by(() => {
    const parts = [plan.skins.length ? `${count(plan.skins.length, 'skin')} (${plan.skins.join(', ')})` : '', plan.models ? count(plan.models, 'model') : '',
      plan.sounds ? count(plan.sounds, 'sound') : '', plan.prefabSettings ? 'its prefab settings' : ''].filter(Boolean);
    return parts.length > 1 ? `${parts.slice(0, -1).join(', ')} and ${parts.at(-1)}` : (parts[0] ?? '');
  });

  onMount(() => {
    const previous = document.activeElement as HTMLElement | null;
    dialog?.focus();
    return () => previous?.focus?.();
  });

  function onKey(e: KeyboardEvent) {
    if (e.key === 'Escape') onCancel();
  }
</script>

<svelte:window onkeydown={onKey} />

<div class="scrim" role="presentation" onclick={onCancel}>
  <div class="dialog" role="dialog" aria-modal="true" aria-label="Delete {plan.name}" tabindex="-1" bind:this={dialog} onclick={(e) => e.stopPropagation()} onkeydown={() => {}}>
    <h2>Delete species '{plan.name}'?</h2>
    <p>{plan.key} leaves this mod{goes ? `, with ${goes}` : ''}.</p>
    <p class="warn">Saved parks with {plan.name} can no longer load its animals, as when its mod is removed. This cannot be undone with Ctrl+Z.</p>
    {#if plan.usedBy.length}
      <div>
        <p>Other mods in this workspace use it; the game skips their parts for it:</p>
        <ul>
          {#each plan.usedBy as user (user.modId)}
            <li>{user.name} ({user.modId}): {[user.skins ? count(user.skins, 'skin') : '', user.models ? count(user.models, 'model') : '', user.sounds ? count(user.sounds, 'sound') : ''].filter(Boolean).join(', ')}</li>
          {/each}
        </ul>
      </div>
    {/if}
    <label class="row"><input type="checkbox" bind:checked={deleteFiles} /> Also move its files to the Recycle Bin: PNGs, models, sounds, language files, icon and Blender projects (never ones something else in the mod uses)</label>
    <div class="actions">
      <button onclick={onCancel}>Cancel</button>
      <button class="primary" onclick={() => onConfirm(deleteFiles)}>Delete species</button>
    </div>
  </div>
</div>

<style>
  .scrim { position: fixed; inset: 0; z-index: 50; background: #0008; display: grid; place-items: center; }
  .dialog { width: min(480px, calc(100vw - 32px)); background: var(--panel); border: 1px solid var(--border); border-radius: var(--radius); padding: 18px 20px; display: grid; gap: 12px; outline: none; }
  h2, p { margin: 0; }
  ul { margin: 4px 0 0; padding-left: 18px; }
  .row { display: flex; gap: 8px; align-items: flex-start; }
  .actions { display: flex; justify-content: flex-end; gap: 8px; }
</style>
