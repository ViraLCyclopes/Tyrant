<script lang="ts">
  import type { ModCheckReport, ModDetail, ModSkinDto, ModSoundDto } from '$lib/rpc/types.gen';
  import { getSession } from '$lib/stores/session.svelte';
  import ItemMenu, { type ItemAction } from './ItemMenu.svelte';
  import NameDialog from './NameDialog.svelte';
  import { groupNames, moveGroup, moveSkin, moveSkinTo, renameGroup, sections, setGroup, type SkinPlace } from './skinGroups';
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
    /** Saves a change to the arrangement (the editor runs the builder on its latest skins); the list waits for what it returns. */
    onArrange?: (build: (skins: ModSkinDto[]) => SkinPlace[] | null) => unknown;
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
  /**
   * The skins as the list shows them. A move shows at once: live while dragging, then until its save settles, so the list never
   * waits for the save; after that it follows the mod again (and so snaps back if the save failed).
   */
  let shown = $state.raw<ModSkinDto[] | null>(null);
  const listed = $derived(shown ?? detail.skins);
  const skinSections = $derived(sections(listed));
  const ungrouped = $derived(skinSections[0].skins.length);
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

  /** The skins in this order and these groups. */
  function placed(skins: readonly ModSkinDto[], order: readonly SkinPlace[]): ModSkinDto[] {
    const byId = new Map(skins.map((k) => [k.id, k]));
    return order.map((p) => ({ ...byId.get(p.id)!, group: p.group }));
  }

  let latest = 0; // the newest save: only its end hands the list back to the mod
  let saving = 0;

  /** Shows the order at once and saves it (the editor runs `build` on its latest skins, so quick repeats stack). */
  function save(order: SkinPlace[], build: (skins: ModSkinDto[]) => SkinPlace[] | null) {
    shown = placed(listed, order);
    const mine = ++latest;
    saving++;
    void Promise.resolve(onArrange(build)).finally(() => {
      saving--;
      if (mine === latest && !dragging) shown = null;
    });
  }

  function arrange(build: (skins: ModSkinDto[]) => SkinPlace[] | null) {
    const order = build(listed);
    if (order) save(order, build);
  }

  function skinActions(skin: ModSkinDto): ItemAction[] {
    const current = skin.group ?? null;
    return [
      { label: 'Move up', run: () => arrange((skins) => moveSkin(skins, skin.id, -1)), disabled: !moveSkin(listed, skin.id, -1) },
      { label: 'Move down', run: () => arrange((skins) => moveSkin(skins, skin.id, 1)), disabled: !moveSkin(listed, skin.id, 1) },
      ...groupNames(listed)
        .filter((g) => g !== current)
        .map((g) => ({ label: `Move to ${g}`, run: () => arrange((skins) => setGroup(skins, [skin.id], g)) })),
      {
        label: 'Move to a new group…',
        run: () => (naming = { title: 'New group', initial: '', action: 'Move', apply: (name) => arrange((skins) => setGroup(skins, [skin.id], name)) }),
      },
      ...(current === null ? [] : [{ label: 'Remove from group', run: () => arrange((skins) => setGroup(skins, [skin.id], null)) }]),
      { label: 'Remove skin…', run: () => onRemoveSkin(skin.id) },
    ];
  }

  function groupActions(group: string): ItemAction[] {
    const members = (skins: readonly ModSkinDto[]) => skins.filter((k) => k.group === group).map((k) => k.id);
    return [
      {
        label: 'Rename group…',
        run: () => (naming = { title: `Rename ${group}`, initial: group, action: 'Rename', apply: (name) => arrange((skins) => renameGroup(skins, group, name)) }),
      },
      { label: 'Move group up', run: () => arrange((skins) => moveGroup(skins, group, -1)), disabled: !moveGroup(listed, group, -1) },
      { label: 'Move group down', run: () => arrange((skins) => moveGroup(skins, group, 1)), disabled: !moveGroup(listed, group, 1) },
      { label: 'Ungroup', run: () => arrange((skins) => setGroup(skins, members(skins), null)) },
    ];
  }

  function onSkinKey(e: KeyboardEvent, skin: ModSkinDto) {
    if (!e.altKey || (e.key !== 'ArrowUp' && e.key !== 'ArrowDown')) return;
    e.preventDefault();
    const delta = e.key === 'ArrowUp' ? -1 : 1;
    arrange((skins) => moveSkin(skins, skin.id, delta));
  }

  // Dragging a skin: pointer events, not HTML drag and drop (the Windows webview takes those for file drops). A press only
  // becomes a drag past a few pixels, so a click still selects. A copy follows the pointer while the list moves the skin to
  // where it would land; letting go keeps it there and saves.
  const DRAG_START = 5;
  let press: { id: string; x: number; y: number } | null = null;
  let dragging = $state<ModSkinDto | null>(null);
  let pointer = $state({ x: 0, y: 0 });
  let before: { order: string; shown: ModSkinDto[] | null } | null = null; // tells a real move from none, and what to cancel to
  let inside = false;
  let dragged = false; // swallows the click the browser sends after a drag
  let nav = $state<HTMLElement>();
  const draggingGroup = $derived(dragging ? (listed.find((k) => k.id === dragging!.id)?.group ?? null) : undefined);

  const orderOf = (skins: readonly ModSkinDto[]) => skins.map((k) => `${k.id}:${k.group ?? ''}`).join('|');

  function startPress(e: PointerEvent, skin: ModSkinDto) {
    dragged = false;
    if (e.button !== 0) return;
    press = { id: skin.id, x: e.clientX, y: e.clientY };
    // Keeps the pointer's events coming here even if it is let go outside the window.
    (e.currentTarget as Element).setPointerCapture?.(e.pointerId);
  }

  /**
   * What is under the pointer: outside the list; nothing to drop on (a gap, the dragged skin itself); a skin row (before it, or
   * after it in its lower half); or a heading (the end of its section).
   */
  function dropAt(x: number, y: number): { inside: boolean; group?: string | null; index?: number } {
    const hit = document.elementFromPoint?.(x, y) ?? null;
    if (!hit || !nav?.contains(hit)) return { inside: false };
    const el = hit.closest<HTMLElement>('[data-drop-group]');
    if (!el || el.dataset.skinId === dragging?.id) return { inside: true };
    const group = el.dataset.dropGroup || null;
    const index = Number(el.dataset.dropIndex);
    if (el.dataset.dropEnd !== undefined) return { inside: true, group, index };
    const box = el.getBoundingClientRect();
    return { inside: true, group, index: y > box.top + box.height / 2 ? index + 1 : index };
  }

  function onPointerMove(e: PointerEvent) {
    if (!press) return;
    if (!dragging) {
      if (Math.hypot(e.clientX - press.x, e.clientY - press.y) < DRAG_START) return;
      const id = press.id;
      dragging = listed.find((k) => k.id === id) ?? null;
      if (!dragging) {
        press = null;
        return;
      }
      before = { order: orderOf(listed), shown };
      shown = listed;
    }
    pointer = { x: e.clientX, y: e.clientY };
    const spot = dropAt(e.clientX, e.clientY);
    inside = spot.inside;
    if (spot.index === undefined) return;
    const next = moveSkinTo(listed, dragging.id, spot.group ?? null, spot.index);
    if (next) shown = placed(listed, next);
  }

  function onPointerUp() {
    press = null;
    if (!dragging) return;
    dragging = null;
    dragged = true;
    if (!inside || orderOf(listed) === before?.order) {
      cancel();
      return;
    }
    before = null;
    const order = listed.map((k) => ({ id: k.id, group: k.group ?? null }));
    save(order, () => order);
  }

  /** Back to the list as it was before the drag (with any save still running shown). */
  function cancel() {
    shown = saving > 0 ? (before?.shown ?? null) : null;
    before = null;
  }

  function cancelDrag() {
    press = null;
    if (!dragging) return;
    dragging = null;
    dragged = true;
    cancel();
  }

  function onKey(e: KeyboardEvent) {
    if (e.key === 'Escape' && dragging) cancelDrag();
  }

  function select(skin: ModSkinDto) {
    if (dragged) {
      dragged = false;
      return;
    }
    onSelect({ kind: 'skin', id: skin.id });
  }
</script>

<svelte:window onpointermove={onPointerMove} onpointerup={onPointerUp} onpointercancel={cancelDrag} onkeydown={onKey} />

<nav class="side" class:dragging={dragging !== null} aria-label="Mod contents" bind:this={nav}>
  <button class:on={selection.kind === 'details'} onclick={() => onSelect({ kind: 'details' })}>Mod details</button>
  <button class:on={selection.kind === 'speciesDatabase'} onclick={() => onSelect({ kind: 'speciesDatabase' })}>Species database</button>

  <div class="heading" data-drop-group="" data-drop-index={ungrouped} data-drop-end>
    <span>Skins ({detail.skins.length})</span>
    <button class="add" aria-label="Add a skin" onclick={() => onAddSkin(null)}>+ Add</button>
  </div>
  {#each skinSections as section (section.group ?? '')}
    {@const open = section.group === null || !collapsed.includes(section.group)}
    {@const count = section.skins.length}
    {#if section.group !== null}
      {@const group = section.group}
      <div class="row group" class:drop-into={!open && draggingGroup === group} data-drop-group={group} data-drop-index={count} data-drop-end>
        <button class="toggle" aria-label="{group} ({count})" aria-expanded={open} onclick={() => toggle(group)}>{open ? '▾' : '▸'} {group} <span class="species">({count})</span></button>
        <button class="add" aria-label="Add a skin to {group}" onclick={() => onAddSkin(group)}>+</button>
        <ItemMenu label="Actions for group {group}" items={() => groupActions(group)} />
      </div>
    {/if}
    {#if open}
      {#each section.skins as skin, i (skin.id)}
        <div class="row" class:in-group={section.group !== null} class:placeholder={dragging?.id === skin.id}
          data-drop-group={section.group ?? ''} data-drop-index={i} data-skin-id={skin.id}>
          <button class="item" class:on={isSkin(skin.id)} aria-label={skin.name} title="{skin.name} · {skin.species} (drag it, or Alt+↑/↓, to move it)"
            onpointerdown={(e) => startPress(e, skin)} onclick={() => select(skin)} onkeydown={(e) => onSkinKey(e, skin)}>
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

{#if dragging}
  <div class="ghost" style:left="{pointer.x + 14}px" style:top="{pointer.y - 12}px" aria-hidden="true">
    {dragging.name} <span class="species">{dragging.species}</span>
  </div>
{/if}

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
  .side.dragging { cursor: grabbing; user-select: none; }
  .row.placeholder { outline: 1px dashed var(--accent); outline-offset: -1px; }
  .row.placeholder > * { opacity: 0.35; }
  .ghost { position: fixed; z-index: 60; pointer-events: none; padding: 5px 10px; border-radius: 5px; white-space: nowrap; background: var(--panel-2); border: 1px solid var(--accent); box-shadow: 0 6px 18px #0008; transform: rotate(-1.5deg); }
  .drop-into { outline: 2px solid var(--accent); outline-offset: -2px; border-radius: 5px; }
  .count { color: var(--warn-text); }
  .check.on .count { color: inherit; }
</style>
