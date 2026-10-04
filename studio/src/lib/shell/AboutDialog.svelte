<script lang="ts">
  import { onMount } from 'svelte';
  import type { AppInfo } from '$lib/rpc/types.gen';
  import { getSession } from '$lib/stores/session.svelte';

  let { onClose }: { onClose: () => void } = $props();
  const session = getSession();
  let info = $state<AppInfo | null>(null);

  onMount(() => {
    session.rpc.call('app.info').then(
      (r) => (info = r),
      () => (info = null), // the dialog still says what Tyrant is
    );
  });

  function onKey(e: KeyboardEvent) {
    if (e.key === 'Escape') onClose();
  }
</script>

<svelte:window onkeydown={onKey} />

<div class="scrim" role="presentation" onclick={onClose}>
  <div class="dialog" role="dialog" aria-modal="true" aria-label="About Tyrant" tabindex="-1" onclick={(e) => e.stopPropagation()} onkeydown={() => {}}>
    <h2>Tyrant</h2>
    {#if info}<p class="hint">Version {info.version} · protocol {info.protocolVersion} · {info.runtime}</p>{/if}
    <p>A modding toolset for Prehistoric Kingdom. It never replaces game files: mods are loaded by Tyrant's own framework and can be turned off or removed.</p>
    <p class="hint">The start-screen art is Prehistoric Kingdom's main-menu background and in-game screenshots by ViraLCyclopes, used with credit to the game's owners (see NOTICE).</p>
    <div class="actions"><button class="primary" onclick={onClose}>Close</button></div>
  </div>
</div>

<style>
  .scrim { position: fixed; inset: 0; z-index: 50; background: #0008; display: grid; place-items: center; }
  .dialog { width: min(460px, calc(100vw - 32px)); background: var(--panel); border: 1px solid var(--border); border-radius: var(--radius); padding: 18px 20px; display: grid; gap: 10px; outline: none; }
  h2 { margin: 0; color: var(--accent); }
  p { margin: 0; }
  .actions { display: flex; justify-content: flex-end; }
</style>
