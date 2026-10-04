<script lang="ts">
  import type { ModCheckReport, ModDetail } from '$lib/rpc/types.gen';
  import type { Selection } from './selection';

  let { detail, check, selection, onSelect, onAddSkin }:
    { detail: ModDetail; check: ModCheckReport | null; selection: Selection; onSelect: (s: Selection) => void; onAddSkin: () => void } = $props();

  const problems = $derived((check?.errors.length ?? 0) + (check?.warnings.length ?? 0));
  const isSkin = (id: string) => selection.kind === 'skin' && selection.id === id;
  const isReplace = (texture: string) => selection.kind === 'replace' && selection.texture === texture;
  const isModel = (target: string) => selection.kind === 'model' && selection.skin === null && selection.target === target;
  const speciesModels = $derived(detail.models.filter((m) => m.skin === null));
</script>

<nav class="side" aria-label="Mod contents">
  <button class:on={selection.kind === 'details'} onclick={() => onSelect({ kind: 'details' })}>Mod details</button>

  <div class="heading">
    <span>Skins ({detail.skins.length})</span>
    <button class="add" aria-label="Add a skin" onclick={onAddSkin}>+ Add</button>
  </div>
  {#each detail.skins as skin (skin.id)}
    <button class:on={isSkin(skin.id)} onclick={() => onSelect({ kind: 'skin', id: skin.id })}>{skin.name}</button>
  {:else}
    <p class="empty">No skins yet.</p>
  {/each}

  <div class="heading"><span>Texture replacements ({detail.replace.length})</span></div>
  {#each detail.replace as replacement (replacement.texture)}
    <button class:on={isReplace(replacement.texture)} onclick={() => onSelect({ kind: 'replace', texture: replacement.texture })}>{replacement.texture}</button>
  {:else}
    <p class="empty">None.</p>
  {/each}

  <div class="heading"><span>Models ({speciesModels.length})</span></div>
  {#each speciesModels as model (model.target)}
    <button class:on={isModel(model.target)} onclick={() => onSelect({ kind: 'model', target: model.target, skin: null })}>
      {model.target}{#if model.errors.length || model.stale}<span class="count">⚠</span>{/if}
    </button>
  {:else}
    <p class="empty">None.</p>
  {/each}

  <button class="check" class:on={selection.kind === 'check'} aria-label="Check{problems ? ` (${problems} problem${problems === 1 ? '' : 's'})` : ''}" onclick={() => onSelect({ kind: 'check' })}>
    Check{#if problems}<span class="count">⚠ {problems}</span>{/if}
  </button>
</nav>

<style>
  .side { display: flex; flex-direction: column; gap: 2px; background: var(--panel); border: 1px solid var(--border); border-radius: var(--radius); padding: 8px; align-self: start; }
  .side > button { text-align: left; border: none; background: none; padding: 5px 8px; border-radius: 5px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .side > button:hover { background: var(--panel-2); }
  .side > button.on { background: var(--accent); color: var(--accent-text); }
  .heading { display: flex; justify-content: space-between; align-items: center; margin-top: 10px; padding: 0 8px; color: var(--muted); font-size: 11px; text-transform: uppercase; letter-spacing: 0.04em; }
  .add { padding: 1px 6px; font-size: 11px; text-transform: none; }
  .empty { margin: 2px 8px; color: var(--muted); font-size: 12px; }
  .check { margin-top: 10px; display: flex; justify-content: space-between; }
  .count { color: var(--warn-text); }
  .check.on .count { color: inherit; }
</style>
