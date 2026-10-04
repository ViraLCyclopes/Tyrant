<script lang="ts">
  import { onMount } from 'svelte';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import AddSkin from '../AddSkin.svelte';
  import { installMod, removeFromGame, restoreCutouts } from '../modActions';
  import CheckPage from './CheckPage.svelte';
  import ModDetailsPage from './ModDetailsPage.svelte';
  import ModelPage from './ModelPage.svelte';
  import { ModDoc } from './modDoc.svelte';
  import ModSideList from './ModSideList.svelte';
  import ReplacementPage from './ReplacementPage.svelte';
  import type { Selection } from './selection';
  import SkinPage from './SkinPage.svelte';

  const session = getSession();
  const tab = getTab();
  const doc = new ModDoc(tab.key ?? '', session.rpc, tab);
  let selection = $state<Selection>({ kind: 'details' });
  let failed = $state(false);
  let adding = $state(false);

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

  tab.undo = { canUndo: () => doc.canUndo, canRedo: () => doc.canRedo, undo: () => void doc.undo(), redo: () => void doc.redo() };
  tab.menus = [
    {
      label: 'Mod',
      items: () => [
        { label: 'Install to game', run: () => void install(), enabled: () => !!doc.detail && !session.busy },
        { label: 'Check', run: () => void doc.runCheck(), enabled: () => !!doc.detail },
        { label: 'Restore cutouts', run: () => void restore(), enabled: () => !!doc.check?.missingCutouts?.length },
        { label: 'Open folder', run: () => void (doc.detail && session.platform.reveal(doc.detail.dir)), enabled: () => !!doc.detail },
        { label: 'Remove from game', run: () => void (doc.detail && removeFromGame(session, tab, doc.id, doc.detail.name)), enabled: () => !!doc.detail && !session.busy },
      ],
    },
  ];

  async function install() {
    if (doc.detail) await installMod(session, tab, doc.id, doc.detail.name);
  }

  async function restore() {
    if (await restoreCutouts(session, tab, doc.id)) await doc.reload();
  }

  async function skinAdded() {
    adding = false;
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
  });

  const selectedSkin = $derived(selection.kind === 'skin' ? doc.detail?.skins.find((k) => k.id === (selection as { id: string }).id) : undefined);
  const selectedModel = $derived.by(() => {
    const s = selection;
    return s.kind === 'model' ? doc.detail?.models.find((m) => m.target === s.target && m.skin === s.skin) : undefined;
  });
  const selectedReplacement = $derived(
    selection.kind === 'replace' ? doc.detail?.replace.find((r) => r.texture === (selection as { texture: string }).texture) : undefined,
  );
</script>

{#if failed}
  <p class="warn">This mod could not be opened; the log says why. <button onclick={load}>Try again</button></p>
{:else if doc.detail}
  <div class="editor">
    <ModSideList detail={doc.detail} check={doc.check} {selection} onSelect={(s) => { adding = false; selection = s; }} onAddSkin={() => (adding = true)} />
    <section class="page">
      {#if adding}
        <AddSkin modId={doc.id} onDone={skinAdded} />
      {:else if selection.kind === 'details'}
        <ModDetailsPage {doc} />
      {:else if selectedSkin}
        <SkinPage {doc} skin={selectedSkin} onOpenModel={(target, skin) => (selection = { kind: 'model', target, skin })} />
      {:else if selectedModel}
        <ModelPage {doc} model={selectedModel} onRemoved={() => (selection = { kind: 'details' })} />
      {:else if selectedReplacement}
        <ReplacementPage {doc} replacement={selectedReplacement} onRemoved={() => (selection = { kind: 'details' })} />
      {:else if selection.kind === 'check'}
        <CheckPage {doc} />
      {/if}
      <div class="actions">
        <button class="primary" onclick={install} disabled={session.busy}>Install to game</button>
        <button onclick={() => { adding = false; selection = { kind: 'check' }; }}>Show Check</button>
      </div>
    </section>
  </div>
{:else}
  <p class="hint">Loading the mod…</p>
{/if}

<style>
  .editor { display: grid; grid-template-columns: 220px 1fr; gap: 16px; }
  .page { min-width: 0; display: grid; gap: 14px; align-content: start; }
  .actions { display: flex; gap: 8px; }
  @media (max-width: 720px) {
    .editor { grid-template-columns: 1fr; }
  }
</style>
