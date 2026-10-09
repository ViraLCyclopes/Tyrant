<script lang="ts">
  import type { ModCheckReport, ModDetail, ModSkinDto, ModSoundDto } from '$lib/rpc/types.gen';
  import { getSession } from '$lib/stores/session.svelte';
  import ItemMenu, { type ItemAction } from './ItemMenu.svelte';
  import NameDialog from './NameDialog.svelte';
  import { groupNames, moveGroup, moveSkin, renameGroup, sections, setGroup, type SkinPlace } from './skinGroups';
  import type { Selection } from './selection';

  let {
    detail,
    check,
    selection,
    onSelect,
    onAddSkin,
    onArrange = () => {},
    onRemoveSkin = () => {},
    onAddModel = () => {},
    onAddSound = () => {},
    onAddReplacement = () => {},
  }: {
    detail: ModDetail;
    check: ModCheckReport | null;
    selection: Selection;
    onSelect: (s: Selection) => void;
    onAddSkin: (group: string | null) => void;
    /** Asks for a change to the arrangement; the editor runs the builder on the latest skins, so quick repeats stack. */
    onArrange?: (build: (skins: ModSkinDto[]) => SkinPlace[] | null) => void;
    onRemoveSkin?: (id: string) => void;
    onAddModel?: () => void;
    onAddSound?: () => void;
    onAddReplacement?: () => void;
  } = $props();

  const problems = $derived((check?.errors.length ?? 0) + (check?.warnings.length ?? 0));
  const isSkin = (id: string) => selection.kind === 'skin' && selection.id === id;
  const isReplace = (texture: string) => selection.kind === 'replace' && selection.texture === texture;
  const isModel = (target: string) => selection.kind === 'model' && selection.skin === null && selection.target === target;
  const speciesModels = $derived(detail.models.filter((m) => m.skin === null));
  const isSound = (s: ModSoundDto) =>
    selection.kind === 'sound' && selection.event === s.event && selection.species === s.species && selection.skin === s.skin;
  const whoHears = (s: ModSoundDto) => s.species ?? (s.skin ? `skin ${s.skin}` : 'everyone');

  const session = getSession();
  const skinSections = $derived(sections(detail.skins));
  const collapsedKey = $derived(`tyrant.mods.collapsedGroups.${detail.id}`);
  let collapsed = $state<string[]>([]);
  let naming = $state<{ title: string; initial: string; action: string; apply: (name: string) => void } | null>(null);

  // Folded groups are a per-mod convenience: remembered in local storage, the list works without it.
  $effect(() => {
    try {
      const saved = JSON.parse(session.store.get(collapsedKey) ?? '[]');
      collapsed = Array.isArray(saved) ? saved.filter((g): g is string => typeof g === 'string') : [];
    } catch {
      collapsed = [];
    }
  });

  function toggle(group: string) {
    collapsed = collapsed.includes(group) ? collapsed.filter((g) => g !== group) : [...collapsed, group];
    session.store.set(collapsedKey, JSON.stringify(collapsed));
  }

  function skinActions(skin: ModSkinDto): ItemAction[] {
    const current = skin.group ?? null;
    return [
      { label: 'Move up', run: () => onArrange((skins) => moveSkin(skins, skin.id, -1)), disabled: !moveSkin(detail.skins, skin.id, -1) },
      { label: 'Move down', run: () => onArrange((skins) => moveSkin(skins, skin.id, 1)), disabled: !moveSkin(detail.skins, skin.id, 1) },
      ...groupNames(detail.skins)
        .filter((g) => g !== current)
        .map((g) => ({ label: `Move to ${g}`, run: () => onArrange((skins) => setGroup(skins, [skin.id], g)) })),
      {
        label: 'Move to a new group…',
        run: () => (naming = { title: 'New group', initial: '', action: 'Move', apply: (name) => onArrange((skins) => setGroup(skins, [skin.id], name)) }),
      },
      ...(current === null ? [] : [{ label: 'Remove from group', run: () => onArrange((skins) => setGroup(skins, [skin.id], null)) }]),
      { label: 'Remove skin…', run: () => onRemoveSkin(skin.id) },
    ];
  }

  function groupActions(group: string): ItemAction[] {
    const members = detail.skins.filter((k) => k.group === group).map((k) => k.id);
    return [
      {
        label: 'Rename group…',
        run: () => (naming = { title: `Rename ${group}`, initial: group, action: 'Rename', apply: (name) => onArrange((skins) => renameGroup(skins, group, name)) }),
      },
      { label: 'Move group up', run: () => onArrange((skins) => moveGroup(skins, group, -1)), disabled: !moveGroup(detail.skins, group, -1) },
      { label: 'Move group down', run: () => onArrange((skins) => moveGroup(skins, group, 1)), disabled: !moveGroup(detail.skins, group, 1) },
      { label: 'Ungroup', run: () => onArrange((skins) => setGroup(skins, members, null)) },
    ];
  }

  function onSkinKey(e: KeyboardEvent, skin: ModSkinDto) {
    if (!e.altKey || (e.key !== 'ArrowUp' && e.key !== 'ArrowDown')) return;
    e.preventDefault();
    const delta = e.key === 'ArrowUp' ? -1 : 1;
    onArrange((skins) => moveSkin(skins, skin.id, delta));
  }
</script>

<nav class="side" aria-label="Mod contents">
  <button class:on={selection.kind === 'details'} onclick={() => onSelect({ kind: 'details' })}>Mod details</button>
  <button class:on={selection.kind === 'speciesDatabase'} onclick={() => onSelect({ kind: 'speciesDatabase' })}>Species database</button>

  <div class="heading">
    <span>Skins ({detail.skins.length})</span>
    <button class="add" aria-label="Add a skin" onclick={() => onAddSkin(null)}>+ Add</button>
  </div>
  {#each skinSections as section (section.group ?? '')}
    {#if section.group !== null}
      {@const group = section.group}
      {@const open = !collapsed.includes(group)}
      <div class="row group">
        <button class="toggle" aria-label="{group} ({section.skins.length})" aria-expanded={open} onclick={() => toggle(group)}>{open ? '▾' : '▸'} {group} <span class="species">({section.skins.length})</span></button>
        <button class="add" aria-label="Add a skin to {group}" onclick={() => onAddSkin(group)}>+</button>
        <ItemMenu label="Actions for group {group}" items={() => groupActions(group)} />
      </div>
    {/if}
    {#if section.group === null || !collapsed.includes(section.group)}
      {#each section.skins as skin (skin.id)}
        <div class="row" class:in-group={section.group !== null}>
          <button class="item" class:on={isSkin(skin.id)} aria-label={skin.name} title="{skin.name} · {skin.species} (Alt+↑/↓ moves it)"
            onclick={() => onSelect({ kind: 'skin', id: skin.id })} onkeydown={(e) => onSkinKey(e, skin)}>
            {skin.name} <span class="species">{skin.species}</span>
          </button>
          <ItemMenu label="Actions for {skin.name}" items={() => skinActions(skin)} />
        </div>
      {/each}
    {/if}
  {/each}
  {#if detail.skins.length === 0}<p class="empty">No skins yet.</p>{/if}

  <div class="heading">
    <span>Texture replacements ({detail.replace.length})</span>
    <button class="add" aria-label="Add a texture replacement" onclick={onAddReplacement}>+ Add</button>
  </div>
  {#each detail.replace as replacement (replacement.texture)}
    <button class:on={isReplace(replacement.texture)} onclick={() => onSelect({ kind: 'replace', texture: replacement.texture })}>{replacement.texture}</button>
  {:else}
    <p class="empty">None.</p>
  {/each}

  <div class="heading">
    <span>Models ({speciesModels.length})</span>
    <button class="add" aria-label="Add a model" onclick={onAddModel}>+ Add</button>
  </div>
  {#each speciesModels as model (model.target)}
    <button class:on={isModel(model.target)} onclick={() => onSelect({ kind: 'model', target: model.target, skin: null })}>
      {model.target}{#if !model.file} <span class="hint">rig edit</span>{:else if model.rig} <span class="hint">+ rig edit</span>{/if}{#if model.errors.length || model.stale}<span class="count">⚠</span>{/if}
    </button>
  {:else}
    <p class="empty">None.</p>
  {/each}

  <div class="heading">
    <span>Sounds ({detail.sounds.length})</span>
    <button class="add" aria-label="Add a sound" onclick={onAddSound}>+ Add</button>
  </div>
  {#each detail.sounds as sound (`${sound.event}|${sound.species}|${sound.skin}`)}
    <button class:on={isSound(sound)} title={sound.event} onclick={() => onSelect({ kind: 'sound', event: sound.event, species: sound.species, skin: sound.skin })}>
      {sound.name} — {whoHears(sound)}
    </button>
  {:else}
    <p class="empty">None yet.</p>
  {/each}

  <button class="check" class:on={selection.kind === 'check'} aria-label="Check{problems ? ` (${problems} problem${problems === 1 ? '' : 's'})` : ''}" onclick={() => onSelect({ kind: 'check' })}>
    Check{#if problems}<span class="count">⚠ {problems}</span>{/if}
  </button>
</nav>

{#if naming}
  <NameDialog title={naming.title} label="Group name" initial={naming.initial} action={naming.action} onCancel={() => (naming = null)}
    onConfirm={(name) => { const apply = naming?.apply; naming = null; apply?.(name); }} />
{/if}

<style>
  .side { display: flex; flex-direction: column; gap: 2px; background: var(--panel); border: 1px solid var(--border); border-radius: var(--radius); padding: 8px; align-self: start; }
  .side > button { text-align: left; border: none; background: none; padding: 5px 8px; border-radius: 5px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .side > button:hover { background: var(--panel-2); }
  .side > button.on { background: var(--accent); color: var(--accent-text); }
  .heading { display: flex; justify-content: space-between; align-items: center; margin-top: 10px; padding: 0 8px; color: var(--muted); font-size: 11px; text-transform: uppercase; letter-spacing: 0.04em; }
  .add { padding: 1px 6px; font-size: 11px; text-transform: none; }
  .empty { margin: 2px 8px; color: var(--muted); font-size: 12px; }
  .check { margin-top: 10px; display: flex; justify-content: space-between; }
  .row { display: flex; align-items: center; gap: 2px; border-radius: 5px; }
  .row .item, .row .toggle { flex: 1; min-width: 0; text-align: left; border: none; background: none; padding: 5px 8px; border-radius: 5px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .row .item:hover, .row .toggle:hover { background: var(--panel-2); }
  .row .item.on { background: var(--accent); color: var(--accent-text); }
  .row.in-group .item { padding-left: 22px; }
  .row.group .toggle { font-weight: 600; }
  .species { color: var(--muted); font-size: 11px; }
  .item.on .species { color: inherit; opacity: 0.8; }
  .count { color: var(--warn-text); }
  .check.on .count { color: inherit; }
</style>
