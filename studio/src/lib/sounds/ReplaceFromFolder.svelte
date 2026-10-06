<script lang="ts">
  import type { SoundsMatchFolderResult } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';

  /**
   * Replace from folder: a sound pack's files matched to this species' sounds by name ("AlloAnax_VoxAngry_03.wav" is take 3
   * of Angry), shown to tick before anything changes; onReplace saves the ticked ones in one edit.
   */
  let { species, onReplace }: {
    species: string;
    onReplace: (sounds: { event: string; files: string[] }[]) => Promise<boolean>;
  } = $props();
  const session = getSession();
  const tab = getTab();

  let match = $state.raw<SoundsMatchFolderResult | null>(null);
  /** Per group: ticked, and the sound picked (preset when only one fits). */
  let ticked = $state<Record<string, boolean>>({});
  let picked = $state<Record<string, string>>({});
  let saving = $state(false);

  const baseName = (file: string) => file.split(/[\\/]/).pop() ?? file;
  const chosen = $derived(
    (match?.groups ?? []).filter((g) => ticked[g.name] && picked[g.name]).map((g) => ({ event: picked[g.name], files: g.files })),
  );

  async function chooseFolder() {
    const folder = await session.platform.pickFolder(`Sounds for ${species} (named like the game's, e.g. AlloAnax_VoxAngry_01.wav)`);
    if (!folder) return;
    const result = await tab.safely(() => session.rpc.call('sounds.matchFolder', { folder, species }));
    if (!result) return;
    ticked = Object.fromEntries(result.groups.map((g) => [g.name, true]));
    picked = Object.fromEntries(result.groups.map((g) => [g.name, g.sounds.length === 1 ? g.sounds[0].event : '']));
    match = result;
  }

  async function replace() {
    saving = true;
    try {
      if (await onReplace(chosen)) match = null;
    } finally {
      saving = false;
    }
  }
</script>

<div class="from-folder">
  <button onclick={chooseFolder}>Replace from folder…</button>
  {#if match}
    {#if match.groups.length === 0}
      <p class="hint">No file in that folder is named like one of {species}'s sounds.</p>
    {:else}
      <table>
        <thead><tr><th></th><th>Files</th><th>Replaces</th></tr></thead>
        <tbody>
          {#each match.groups as group (group.name)}
            <tr>
              <td><input type="checkbox" aria-label="Replace with {group.name}" bind:checked={ticked[group.name]} /></td>
              <td><span class="name">{group.name}</span> <span class="hint">({group.files.length} file{group.files.length === 1 ? '' : 's'})</span></td>
              <td>
                {#if group.sounds.length === 1}
                  {group.sounds[0].name} <span class="hint">({group.sounds[0].group})</span>
                {:else}
                  <select aria-label="Sound for {group.name}" bind:value={picked[group.name]}>
                    <option value="" disabled>Fits {group.sounds.length} sounds: pick one…</option>
                    {#each group.sounds as s (s.event)}<option value={s.event}>{s.name} ({s.event.split('/').at(-1)})</option>{/each}
                  </select>
                {/if}
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
      <p class="hint">Several files for one sound: one plays at random each time, as the game does with its own takes.</p>
    {/if}
    {#if match.unmatched.length}
      <p class="hint">Not matched (left out): {match.unmatched.map(baseName).join(', ')}</p>
    {/if}
    <div class="row">
      <button class="primary" disabled={chosen.length === 0 || saving} onclick={replace}>
        Replace {chosen.length} sound{chosen.length === 1 ? '' : 's'}
      </button>
      <button onclick={() => (match = null)}>Cancel</button>
    </div>
  {/if}
</div>

<style>
  .from-folder { margin: 8px 0; }
  table { border-collapse: collapse; margin: 8px 0; }
  td, th { padding: 2px 8px; text-align: left; }
  .name { font-family: var(--mono); font-size: 12px; }
  .row { display: flex; gap: 8px; }
</style>
