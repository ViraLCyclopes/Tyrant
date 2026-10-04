<script lang="ts">
  import { onMount } from 'svelte';
  import type { ShellState } from './shellState.svelte';

  let { shell, introHidden, onIntro, onClose }: { shell: ShellState; introHidden: boolean; onIntro: (hidden: boolean) => void; onClose: () => void } = $props();
  let dialog = $state<HTMLElement>();

  onMount(() => {
    const previous = document.activeElement as HTMLElement | null;
    dialog?.focus();
    return () => previous?.focus?.();
  });

  function onKey(e: KeyboardEvent) {
    if (e.key === 'Escape') onClose();
  }
</script>

<svelte:window onkeydown={onKey} />

<div class="scrim" role="presentation" onclick={onClose}>
  <div class="dialog" role="dialog" aria-modal="true" aria-label="Preferences" tabindex="-1" bind:this={dialog} onclick={(e) => e.stopPropagation()} onkeydown={() => {}}>
    <h2>Preferences</h2>
    <label class="row">
      <input type="checkbox" checked={!introHidden} onchange={(e) => onIntro(!e.currentTarget.checked)} />
      <span><strong>Show the intro on Home</strong><br /><span class="hint">The short "what is Tyrant" card over the start screen.</span></span>
    </label>
    <label class="row">
      <input type="checkbox" checked={shell.prefs.checkUpdates} onchange={(e) => shell.setPrefs({ checkUpdates: e.currentTarget.checked })} />
      Check for updates when Tyrant starts
    </label>
    <label class="row">
      <input type="checkbox" checked={shell.prefs.reopenTabs} onchange={(e) => shell.setPrefs({ reopenTabs: e.currentTarget.checked })} />
      <span><strong>Reopen my tabs on start</strong><br /><span class="hint">Tyrant opens the tabs you had open last time (their filters start fresh).</span></span>
    </label>
    <fieldset class="row">
      <legend><strong>Log position</strong></legend>
      <label><input type="radio" name="log-position" checked={shell.prefs.logPosition === 'bottom'} onchange={() => shell.setPrefs({ logPosition: 'bottom' })} /> At the bottom</label>
      <label><input type="radio" name="log-position" checked={shell.prefs.logPosition === 'side'} onchange={() => shell.setPrefs({ logPosition: 'side' })} /> At the side</label>
    </fieldset>
    <div class="actions"><button class="primary" onclick={onClose}>Done</button></div>
  </div>
</div>

<style>
  .scrim { position: fixed; inset: 0; z-index: 50; background: #0008; display: grid; place-items: center; }
  .dialog { width: min(480px, calc(100vw - 32px)); background: var(--panel); border: 1px solid var(--border); border-radius: var(--radius); padding: 18px 20px; display: grid; gap: 14px; outline: none; }
  h2 { margin: 0; }
  .row { display: flex; gap: 10px; align-items: flex-start; }
  fieldset.row { flex-direction: column; border: none; padding: 0; margin: 0; gap: 4px; }
  .actions { display: flex; justify-content: flex-end; }
</style>
