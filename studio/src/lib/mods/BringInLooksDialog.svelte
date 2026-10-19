<script lang="ts">
  import { onMount, untrack } from 'svelte';
  import type { LooksAnswer, LooksPromptMod } from '$lib/stores/session.svelte';

  // Before install, remove, delete or uninstall throws away the game's copy: which looks made in the game to keep.
  let {
    action,
    mods,
    onContinue,
    onCancel,
  }: { action: string; mods: LooksPromptMod[]; onContinue: (answer: LooksAnswer) => void; onCancel: () => void } = $props();

  const key = (id: string, folder: string) => `${id}\n${folder}`;
  // Bring in: on by default. Where both sides changed, the game's copy by default.
  let bring = $state<Record<string, boolean>>(
    untrack(() => Object.fromEntries(mods.flatMap((m) => m.changes.filter((c) => !c.projectChanged).map((c) => [key(m.id, c.folder), true])))),
  );
  let side = $state<Record<string, 'game' | 'project'>>(
    untrack(() => Object.fromEntries(mods.flatMap((m) => m.changes.filter((c) => c.projectChanged).map((c) => [key(m.id, c.folder), 'game' as const])))),
  );
  let copy = $state<Record<string, boolean>>(untrack(() => Object.fromEntries(mods.filter((m) => m.gameOnly).map((m) => [m.id, true]))));
  let dialog = $state<HTMLElement>();

  onMount(() => {
    const previous = document.activeElement as HTMLElement | null;
    dialog?.focus();
    return () => previous?.focus?.();
  });

  function onKey(e: KeyboardEvent) {
    if (e.key === 'Escape') onCancel();
  }

  /** "materials/skins/blue" → "Skin blue"; "materials/vanilla-skins/Carcharodontosaurus-Alt 1" → "Vanilla skin Carcharodontosaurus-Alt 1". */
  function label(folder: string): string {
    const [, kind, name] = folder.split('/');
    return `${kind === 'skins' ? 'Skin' : kind === 'species' ? 'Species' : 'Vanilla skin'} ${name ?? folder}`;
  }

  function finish() {
    const bringIn: Record<string, string[]> = {};
    for (const mod of mods.filter((m) => !m.gameOnly))
      bringIn[mod.id] = mod.changes
        .filter((c) => (c.projectChanged ? side[key(mod.id, c.folder)] === 'game' : bring[key(mod.id, c.folder)]))
        .map((c) => c.folder);
    onContinue({ bringIn, copy: mods.filter((m) => m.gameOnly && copy[m.id]).map((m) => m.id) });
  }
</script>

<svelte:window onkeydown={onKey} />

<div class="scrim" role="presentation" onclick={onCancel}>
  <div class="dialog" role="dialog" aria-modal="true" aria-label="Bring in looks made in the game" tabindex="-1" bind:this={dialog} onclick={(e) => e.stopPropagation()} onkeydown={() => {}}>
    <h2>Bring in looks made in the game?</h2>
    {#if action}
      <p>
        {action} replaces or removes the game's copy, and with it the looks the in-game editor saved there. Bring them into the workspace first to
        keep them.
      </p>
    {:else}
      <p>The in-game editor saved these looks into the game's copy. The ones you choose replace the workspace's.</p>
    {/if}
    {#each mods as mod (mod.id)}
      <section>
        <h3>{mod.name}</h3>
        {#if mod.gameOnly}
          <label class="row">
            <input type="checkbox" aria-label="Copy {mod.name} to the workspace first" bind:checked={copy[mod.id]} /> Copy to workspace first
          </label>
          <p class="hint">It is only in the game, so its looks are nowhere else.</p>
        {:else}
          <ul>
            {#each mod.changes as change (change.folder)}
              {@const k = key(mod.id, change.folder)}
              <li>
                <span><strong>{label(change.folder)}</strong> <span class="hint">{change.folder}</span></span>
                {#if change.projectChanged}
                  <span class="choice" role="radiogroup" aria-label="Which {label(change.folder)} to keep">
                    <label><input type="radio" name={k} value="game" aria-label="Game's {change.folder}" bind:group={side[k]} /> Game's</label>
                    <label><input type="radio" name={k} value="project" aria-label="Project's {change.folder}" bind:group={side[k]} /> Project's</label>
                    <span class="hint">changed in the workspace too</span>
                  </span>
                {:else}
                  <label><input type="checkbox" aria-label="Bring in {change.folder}" bind:checked={bring[k]} /> Bring in</label>
                {/if}
              </li>
            {/each}
          </ul>
        {/if}
      </section>
    {/each}
    <div class="actions">
      <button onclick={onCancel}>Cancel</button>
      <button class="primary" onclick={finish}>Continue</button>
    </div>
  </div>
</div>

<style>
  .scrim { position: fixed; inset: 0; z-index: 60; background: #0008; display: grid; place-items: center; }
  .dialog { width: min(560px, calc(100vw - 32px)); max-height: calc(100vh - 32px); overflow: auto; background: var(--panel); border: 1px solid var(--border); border-radius: var(--radius); padding: 18px 20px; display: grid; gap: 12px; outline: none; }
  h2, h3, p { margin: 0; }
  h3 { font-size: 1em; }
  section { display: grid; gap: 6px; }
  ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 6px; }
  li { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 8px; align-items: center; }
  .choice { display: flex; gap: 10px; align-items: center; }
  .row { display: flex; gap: 8px; align-items: flex-start; }
  .actions { display: flex; justify-content: flex-end; gap: 8px; }
</style>
