<script lang="ts">
  import { onMount } from 'svelte';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import AddSkin from '../AddSkin.svelte';
  import DeleteModDialog from '../DeleteModDialog.svelte';
  import ExportDialog from './ExportDialog.svelte';
  import type { AddedSound } from '$lib/sounds/audio';
  import AddModel from './AddModel.svelte';
  import AddPrefabTarget from './AddPrefabTarget.svelte';
  import AddSpeciesMaterial from './AddSpeciesMaterial.svelte';
  import AddVanillaSkinMaterial from './AddVanillaSkinMaterial.svelte';
  import PrefabPage from './PrefabPage.svelte';
  import AddReplacement from './AddReplacement.svelte';
  import AddSound from './AddSound.svelte';
  import { deleteMod, exportMod, installLabel, installMod, removeFromGame, restoreCutouts } from '../modActions';
  import CheckPage from './CheckPage.svelte';
  import ModDetailsPage from './ModDetailsPage.svelte';
  import ModelPage from './ModelPage.svelte';
  import { ModDoc } from './modDoc.svelte';
  import RemoveSkinDialog from './RemoveSkinDialog.svelte';
  import ModSideList from './ModSideList.svelte';
  import ReplacementPage from './ReplacementPage.svelte';
  import type { Selection } from './selection';
  import SkinPage from './SkinPage.svelte';
  import SoundPage from './SoundPage.svelte';
  import SpeciesMaterialPage from './SpeciesMaterialPage.svelte';
  import VanillaSkinMaterialPage from './VanillaSkinMaterialPage.svelte';
  import SpeciesDatabase from './SpeciesDatabase.svelte';

  const session = getSession();
  const tab = getTab();
  const doc = new ModDoc(tab.key ?? '', session.rpc, tab);
  let selection = $state<Selection>({ kind: 'details' });
  let failed = $state(false);
  let addingGroup = $state<string | null>(null);
  let addingSpecies = $state<string | null>(null);
  let removingSkin = $state<string | null>(null);
  /** The form shown instead of a page: + Add on Skins, Models or Sounds. */
  let adding = $state<'skin' | 'model' | 'sound' | 'texture' | 'prefab' | 'material' | 'vanillaMaterial' | null>(null);

  onMount(() => {
    void load();
  });

  async function load() {
    failed = !(await doc.load());
  }

  // The tab's title follows the mod's name.
  $effect(() => {
    if (doc.detail) tab.setTitle(doc.detail.name);
  });

  // Coming back to the tab: take changes made elsewhere (the CLI, a text editor).
  let wasActive = tab.active;
  $effect(() => {
    const active = tab.active;
    if (active && !wasActive) void doc.refreshIfChanged();
    wasActive = active;
  });

  // Coming back to the app window (from Blender after Send to Tyrant): the same refresh, for the tab on show.
  $effect(() => {
    const onFocus = () => {
      if (tab.active) void doc.refreshIfChanged();
    };
    window.addEventListener('focus', onFocus);
    return () => window.removeEventListener('focus', onFocus);
  });

  tab.undo = { canUndo: () => doc.canUndo, canRedo: () => doc.canRedo, undo: () => void doc.undo(), redo: () => void doc.redo() };
  tab.menus = [
    {
      label: 'Mod',
      items: () => [
        { label: installLabel(installState), run: () => void install(), enabled: () => !!doc.detail && !session.busy },
        { label: 'Check', run: () => void doc.runCheck(), enabled: () => !!doc.detail },
        {
          label: 'Export for sharing…',
          run: () => void (exporting = true),
          enabled: () => !!doc.detail && !session.busy,
        },
        { label: 'Restore cutouts', run: () => void restore(), enabled: () => !!doc.check?.missingCutouts?.length },
        { label: 'Open folder', run: () => void (doc.detail && session.platform.reveal(doc.detail.dir)), enabled: () => !!doc.detail },
        { label: 'Remove from game', run: () => void removeFromTheGame(), enabled: () => !!doc.detail && !session.busy },
        { label: 'Delete mod…', run: () => void askDelete(), enabled: () => !!doc.detail && !session.busy },
      ],
    },
  ];

  let deleting = $state<{ installed: boolean } | null>(null);
  /** Export for sharing… asks first whether to include the PNGs. */
  let exporting = $state(false);

  function exportChosen(includePngs: boolean) {
    exporting = false;
    if (doc.detail) void exportMod(session, tab, doc.id, doc.detail.name, doc.detail.version, includePngs);
  }
  let deleteError = $state<string | null>(null);
  let deletingBusy = $state(false);

  async function askDelete() {
    const list = await tab.quietly(() => session.rpc.call('mods.list'));
    const row = list?.mods.find((m) => m.id === doc.id);
    deleteError = null;
    deleting = { installed: row ? row.state !== 'notInstalled' : true }; // unknown: offer it; deleting copes with not installed
  }

  async function confirmDelete(removeFromGame: boolean) {
    if (!doc.detail || deletingBusy) return;
    deletingBusy = true;
    try {
      const r = await deleteMod(session, tab, doc.id, doc.detail.name, removeFromGame);
      if ('error' in r) deleteError = r.error;
      else deleting = null; // deleteMod closed this tab
    } finally {
      deletingBusy = false;
    }
  }

  // Where the mod stands in the game, for the install button's words; looked up again after every edit and install.
  let installState = $state<string | null>(null);
  async function refreshInstallState() {
    const list = await tab.quietly(() => session.rpc.call('mods.list'));
    installState = list?.mods.find((m) => m.id === doc.id)?.state ?? null;
  }
  $effect(() => {
    if (doc.detail?.revision) void refreshInstallState();
  });

  // Both may first bring looks made in the game into this mod's material files: take them (undo stays only if mod.json did not change).
  async function install() {
    if (!doc.detail) return;
    const installed = await installMod(session, tab, doc.id, doc.detail.name);
    await doc.refreshIfChanged();
    if (installed) await refreshInstallState();
  }

  async function removeFromTheGame() {
    if (!doc.detail) return;
    await removeFromGame(session, tab, doc.id, doc.detail.name);
    await doc.refreshIfChanged();
  }

  async function restore() {
    if (await restoreCutouts(session, tab, doc.id)) await doc.reload();
  }

  function modelAdded(target: string | null) {
    adding = null;
    if (target) selection = { kind: 'model', target, skin: null }; // the new model is selected
  }

  async function replacementAdded(texture: string | null) {
    adding = null;
    if (!texture) return;
    await doc.reload(); // added outside the editor's own edits (mods.replace)
    selection = { kind: 'replace', texture };
  }

  async function soundAdded(added: AddedSound | null) {
    adding = null;
    if (!added) return;
    await doc.reload(); // added outside the editor's own edits (the sound lists call the RPC)
    selection = { kind: 'sound', ...added };
  }

  /** Replace from folder: every matched sound in one edit (one undo step), for that species or one skin; the first one opens. */
  async function soundsReplaced(species: string, skin: string | null, sounds: { event: string; files: string[] }[]): Promise<boolean> {
    const scope = skin ? { species: null, skin } : { species, skin: null };
    if (!(await doc.edit('mods.replaceSounds', { ...scope, sounds }))) return false;
    adding = null;
    selection = { kind: 'sound', event: sounds[0].event, ...scope };
    return true;
  }

  async function skinAdded() {
    adding = null;
    addingSpecies = null;
    const before = new Set(doc.detail?.skins.map((s) => s.id));
    await doc.reload();
    const added = doc.detail?.skins.find((s) => !before.has(s.id));
    if (added) selection = { kind: 'skin', id: added.id }; // the new skin is selected
  }

  // A removed skin or replacement cannot stay selected.
  $effect(() => {
    const d = doc.detail;
    if (!d) return;
    const s = selection;
    if (s.kind === 'skin' && !d.skins.some((k) => k.id === s.id)) selection = { kind: 'details' };
    if (s.kind === 'replace' && !d.replace.some((r) => r.texture === s.texture)) selection = { kind: 'details' };
    if (s.kind === 'model' && !d.models.some((m) => m.target === s.target && m.skin === s.skin)) selection = { kind: 'details' };
    if (s.kind === 'sound' && !d.sounds.some((x) => x.event === s.event && x.species === s.species && x.skin === s.skin)) selection = { kind: 'details' };
  });

  const selectedSkin = $derived(selection.kind === 'skin' ? doc.detail?.skins.find((k) => k.id === (selection as { id: string }).id) : undefined);
  const selectedModel = $derived.by(() => {
    const s = selection;
    return s.kind === 'model' ? doc.detail?.models.find((m) => m.target === s.target && m.skin === s.skin) : undefined;
  });
  const selectedSound = $derived.by(() => {
    const s = selection;
    return s.kind === 'sound' ? doc.detail?.sounds.find((x) => x.event === s.event && x.species === s.species && x.skin === s.skin) : undefined;
  });
  const selectedReplacement = $derived(
    selection.kind === 'replace' ? doc.detail?.replace.find((r) => r.texture === (selection as { texture: string }).texture) : undefined,
  );
</script>

{#if failed}
  <p class="warn">This mod could not be opened; the log says why. <button onclick={load}>Try again</button></p>
{:else if doc.detail}
  <div class="editor">
    <ModSideList detail={doc.detail} check={doc.check} {selection} onSelect={(s) => { adding = null; selection = s; }}
      onAddSkin={(group) => { addingGroup = group; addingSpecies = null; adding = 'skin'; }}
      onArrange={(build) => doc.edit('mods.arrangeSkins', (d) => { const order = build(d.skins); return order ? { order } : null; })} onRemoveSkin={(id) => (removingSkin = id)} onAddModel={() => (adding = 'model')} onAddSound={() => (adding = 'sound')}
      onAddReplacement={() => (adding = 'texture')} onAddPrefab={() => (adding = 'prefab')} onAddMaterial={() => (adding = 'material')} onAddVanillaMaterial={() => (adding = 'vanillaMaterial')} />
    <section class="page">
      {#if adding === 'skin'}
        <AddSkin modId={doc.id} group={addingGroup} speciesId={addingSpecies ?? undefined} onDone={skinAdded} />
      {:else if adding === 'model'}
        <AddModel {doc} onDone={modelAdded} />
      {:else if adding === 'sound'}
        <AddSound modId={doc.id} onDone={soundAdded} onReplaceMany={soundsReplaced} modSkins={doc.detail?.skins ?? []} />
      {:else if adding === 'texture'}
        <AddReplacement modId={doc.id} onDone={replacementAdded} />
      {:else if adding === 'prefab'}
        <AddPrefabTarget {doc} onDone={(t) => { adding = null; if (t) selection = { kind: 'prefab', target: t }; }} />
      {:else if adding === 'vanillaMaterial'}
        <AddVanillaSkinMaterial {doc} onDone={(p) => { adding = null; if (p) selection = { kind: 'vanillaMaterial', ...p }; }} />
      {:else if adding === 'material'}
        <AddSpeciesMaterial {doc} onDone={(s) => { adding = null; if (s) selection = { kind: 'material', species: s }; }} />
      {:else if selection.kind === 'details'}
        <ModDetailsPage {doc} />
      {:else if selection.kind === 'speciesDatabase'}
        <SpeciesDatabase {doc} onPrefab={(t) => (selection = { kind: 'prefab', target: t })} onOpenSkin={(id) => (selection = { kind: 'skin', id })}
          onAddSkin={(k) => { addingGroup = null; addingSpecies = k; adding = 'skin'; }} />
      {:else if selectedSkin}
        <SkinPage {doc} skin={selectedSkin} onOpenModel={(target, skin) => (selection = { kind: 'model', target, skin })} />
      {:else if selectedModel}
        <ModelPage {doc} model={selectedModel} onRemoved={() => (selection = { kind: 'details' })} />
      {:else if selectedSound}
        <SoundPage
          {doc}
          sound={selectedSound}
          onRemoved={() => (selection = { kind: 'details' })}
          onScopeChanged={(species, skin) => (selection = { kind: 'sound', event: selectedSound.event, species, skin })}
        />
      {:else if selectedReplacement}
        <ReplacementPage {doc} replacement={selectedReplacement} onRemoved={() => (selection = { kind: 'details' })} />
      {:else if selection.kind === 'prefab'}
        <PrefabPage {doc} target={selection.target} onRemoved={() => (selection = { kind: 'details' })} />
      {:else if selection.kind === 'vanillaMaterial'}
        {#key `${selection.species}/${selection.skin}`}<VanillaSkinMaterialPage {doc} species={selection.species} skin={selection.skin} onRemoved={() => (selection = { kind: 'details' })} />{/key}
      {:else if selection.kind === 'material'}
        {#key selection.species}<SpeciesMaterialPage {doc} species={selection.species} onRemoved={() => (selection = { kind: 'details' })} />{/key}
      {:else if selection.kind === 'check'}
        <CheckPage {doc} />
      {/if}
      <div class="actions">
        <button class="primary" onclick={install} disabled={session.busy}>{installLabel(installState)}</button>
        <button onclick={() => { adding = null; selection = { kind: 'check' }; }}>Show Check</button>
      </div>
    </section>
  </div>
{:else}
  <p class="hint">Loading the mod…</p>
{/if}

{#if exporting && doc.detail}
  <ExportDialog name={doc.detail.name} onexport={exportChosen} oncancel={() => (exporting = false)} />
{/if}
{#if deleting && doc.detail}
  <DeleteModDialog name={doc.detail.name} installed={deleting.installed} error={deleteError} busy={deletingBusy} onCancel={() => (deleting = null)} onConfirm={confirmDelete} />
{/if}

{#if removingSkin && doc.detail}
  {@const skin = doc.detail.skins.find((k) => k.id === removingSkin)}
  {#if skin}
    <RemoveSkinDialog name={skin.name} startingOf={skin.starting ? skin.species : null} onCancel={() => (removingSkin = null)} onConfirm={(deleteFiles) => { const id = skin.id; removingSkin = null; void doc.removeSkin(id, deleteFiles); }} />
  {/if}
{/if}

<style>
  .editor { display: grid; grid-template-columns: 220px 1fr; gap: 16px; }
  .page { min-width: 0; display: grid; gap: 14px; align-content: start; }
  .actions { display: flex; gap: 8px; }
  @media (max-width: 720px) {
    .editor { grid-template-columns: 1fr; }
  }
</style>
