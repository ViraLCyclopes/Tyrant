<script lang="ts">
  import { onMount } from 'svelte';
  import type { ModCheckReport, ModRow, ModsListResult } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import CleanSkins from './CleanSkins.svelte';
  import DeleteModDialog from './DeleteModDialog.svelte';
  import { bringInLooks, copyFromGame, deleteMod, extractTextures, importModZip, installAria, installLabel, installMod, removeFromGame, restoreCutouts as restoreModCutouts } from './modActions';

  const session = getSession();
  const tab = getTab();
  let list = $state<ModsListResult | null>(null);
  let deleting = $state<ModRow | null>(null);
  let deleteError = $state<string | null>(null);
  let deletingBusy = $state(false);

  async function confirmDelete(removeFromGame: boolean) {
    const row = deleting;
    if (!row || deletingBusy) return;
    deletingBusy = true;
    try {
      const r = await deleteMod(session, tab, row.id, row.name, removeFromGame);
      if ('error' in r) deleteError = r.error;
      else {
        list = r.list;
        deleting = null;
      }
    } finally {
      deletingBusy = false;
    }
  }
  let cleaning = $state(false);
  let checks = $state<Record<string, ModCheckReport>>({});
  let newId = $state('');
  let newName = $state('');

  const STATE_LABEL: Record<string, string> = {
    notInstalled: 'Not installed',
    installed: 'Installed',
    changed: 'Changed since install',
    gameOnly: 'In the game only',
  };

  onMount(() => void refresh());

  async function refresh() {
    const r = await tab.quietly(() => session.rpc.call('mods.list'));
    if (r) list = r;
  }

  async function create() {
    const id = newId.trim();
    const name = newName.trim() || null;
    const r = await tab.safely(() => session.rpc.call('mods.create', { id, name, author: null }));
    if (!r) return;
    list = r;
    newId = '';
    newName = '';
    open(id, name ?? id); // a new mod is made in its editor
  }

  function open(id: string, name: string) {
    tab.openTool('mod', { key: id, title: name });
  }

  async function check(row: ModRow) {
    const r = await tab.safely(() => session.rpc.call('mods.check', { id: row.id }));
    if (r) checks = { ...checks, [row.id]: r };
  }

  async function restoreCutouts(row: ModRow) {
    if (await restoreModCutouts(session, tab, row.id)) await check(row);
  }

  async function install(row: ModRow) {
    if (await installMod(session, tab, row.id, row.name)) await refresh();
  }

  async function addFromZip() {
    const mods = await importModZip(session, tab);
    if (mods) list = mods;
  }

  async function copy(row: ModRow) {
    const mods = await copyFromGame(session, tab, row.id);
    if (mods) list = mods;
  }

  async function updateFramework() {
    if (await session.runJob('dump.install', undefined, 'Update Tyrant in game', tab)) await refresh();
  }

  async function bringIn(row: ModRow) {
    if (await bringInLooks(session, tab, row)) await refresh();
  }

  async function remove(row: ModRow) {
    if (await removeFromGame(session, tab, row.id, row.name)) await refresh();
  }

  function summary(row: ModRow): string {
    const parts = [];
    if (row.replacements) parts.push(`Replaces ${row.replacements} texture${row.replacements === 1 ? '' : 's'}`);
    if (row.skins) parts.push(`Adds ${row.skins} skin${row.skins === 1 ? '' : 's'}`);
    return parts.join(' · ') || 'Empty';
  }

  async function setEnabled(row: ModRow, enabled: boolean, box: HTMLInputElement) {
    const r = await tab.safely(() => session.rpc.call('mods.enable', { id: row.id, enabled }));
    if (r) list = r;
    else box.checked = !enabled; // the change did not happen
  }
</script>

<h1>Mods</h1>
<p class="hint">
  A mod is a folder in your workspace (<code>mods\&lt;id&gt;</code>) with a mod.json and its textures. Installing copies it into the game's
  <code>UserData\Tyrant\Mods</code>; Tyrant's framework applies it while the game runs, so no game file is ever replaced. Add textures from the
  Assets tab with <strong>Replace in a mod…</strong>.
</p>

<form class="row new" onsubmit={(e) => { e.preventDefault(); void create(); }}>
  <input aria-label="Mod id" placeholder="mod id, e.g. red-spot-carcharo" bind:value={newId} />
  <input aria-label="Mod name" placeholder="name (optional)" bind:value={newName} />
  <button type="submit" disabled={!newId.trim() || session.busy}>New mod</button>
</form>
{#if list?.frameworkOutdated}
  <p class="warn">Tyrant was updated: the framework in the game is older than this Tyrant. Update it (the game must be closed).</p>
  <button class="primary" onclick={updateFramework} disabled={session.busy}>Update Tyrant in game</button>
{/if}
<button class="ghost" onclick={addFromZip} disabled={session.busy}>Add mod from zip…</button>
<button class="ghost" onclick={() => extractTextures(session, tab)} disabled={session.busy}>Extract textures…</button>
<button class="ghost" onclick={() => (cleaning = !cleaning)}>Clean up skin numbers…</button>
{#if cleaning}<CleanSkins />{/if}

{#if list && !list.frameworkInstalled}
  <p class="hint">Tyrant's framework is not in the game yet; installing a mod installs it.</p>
{/if}

{#if list?.mods.length}
  <table class="grid">
    <thead><tr><th>Mod</th><th>Version</th><th>Content</th><th>State</th><th>On</th><th><span class="visually-hidden">Actions</span></th></tr></thead>
    <tbody>
      {#each list.mods as row (row.id)}
        <tr>
          <td><strong>{row.name}</strong>{#if row.name !== row.id} <span class="hint">{row.id}</span>{/if}{#if row.error}<div class="warn">{row.error}</div>{/if}{#each row.clashes ?? [] as clash (clash)}<div class="hint">{clash}</div>{/each}{#each row.usesSpeciesFrom ?? [] as use (use.modId)}<div class="hint">Uses species from: {use.modName ?? use.modId}{#if !use.installed || !use.enabled} <span class="warn">({use.installed ? 'off' : 'not installed'} — {use.skins} {use.skins === 1 ? 'skin' : 'skins'} skipped)</span>{/if}</div>{/each}</td>
          <td>{row.version}</td>
          <td>{summary(row)}</td>
          <td>{STATE_LABEL[row.state] ?? row.state}{#if row.cooked === 'stale'} <span class="hint">· {row.state === 'gameOnly' ? 'textures not cooked' : 'Reinstall to cook textures'}</span>{/if}{#if row.gameLooks} <span class="hint">· {row.state === 'gameOnly' ? 'has looks (materials)' : 'looks changed in the game'}</span>{/if}</td>
          <td>
            {#if row.enabled !== null}
              <input type="checkbox" aria-label="{row.name} on" checked={row.enabled} onchange={(e) => setEnabled(row, e.currentTarget.checked, e.currentTarget)} />
            {/if}
          </td>
          <td class="actions">
            {#if row.state !== 'gameOnly'}
              <button class="primary" aria-label="Open {row.name}" onclick={() => open(row.id, row.name)}>Open</button>
              <button aria-label="Check {row.name}" onclick={() => check(row)}>Check</button>
              <button aria-label={installAria(row.state, row.name)} onclick={() => install(row)} disabled={session.busy}>{installLabel(row.state)}</button>
              {#if row.gameLooks}<button aria-label="Bring in looks made in the game into {row.name}" onclick={() => bringIn(row)} disabled={session.busy}>Bring in looks…</button>{/if}
            {/if}
            {#if row.state === 'gameOnly'}
              <button aria-label="Copy {row.name} to workspace" onclick={() => copy(row)} disabled={session.busy}>Copy to workspace</button>
            {/if}
            {#if row.state !== 'notInstalled'}
              <button aria-label="Remove {row.name} from the game" onclick={() => remove(row)} disabled={session.busy}>Remove from game</button>
            {/if}
            {#if row.dir}<button class="ghost" aria-label="Open {row.name} folder" onclick={() => session.platform.reveal(row.dir!)}>Open folder</button>{/if}
            {#if row.state !== 'gameOnly'}
              <button class="ghost" aria-label="Delete {row.name}" onclick={() => { deleteError = null; deleting = row; }} disabled={session.busy}>Delete…</button>
            {/if}
          </td>
        </tr>
        {#if checks[row.id]}
          <tr class="check">
            <td colspan="6">
              {#each checks[row.id].errors as error (error)}<div class="warn">{error}</div>{/each}
              {#each checks[row.id].warnings as warning (warning)}<div class="hint">{warning}</div>{/each}
              {#if !checks[row.id].errors.length && !checks[row.id].warnings.length}<div class="hint">No problems found.</div>{/if}
              {#if checks[row.id].missingCutouts?.length}
                <button aria-label="Restore cutouts for {row.name}" onclick={() => restoreCutouts(row)} disabled={session.busy}>Restore cutouts</button>
              {/if}
            </td>
          </tr>
        {/if}
      {/each}
    </tbody>
  </table>
{:else if list}
  <p class="hint">No mods yet. Create one above, or open a texture in the Assets tab and click Replace in a mod….</p>
{/if}

{#if deleting}
  <DeleteModDialog name={deleting.name} installed={deleting.state !== 'notInstalled'} error={deleteError} busy={deletingBusy} onCancel={() => (deleting = null)} onConfirm={confirmDelete} />
{/if}

<style>
  .new { gap: 8px; margin: 12px 0; }
  .actions { display: flex; gap: 6px; flex-wrap: wrap; }
</style>
