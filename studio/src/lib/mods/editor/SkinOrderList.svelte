<script lang="ts">
  import type { ModSkinDto } from '$lib/rpc/types.gen';
  import type { ModDoc } from './modDoc.svelte';
  import { moveBy, moveTo } from './skinOrder';

  let { doc, speciesKey, order, skins, onOpenSkin, onMakeDefault }: {
    doc: ModDoc;
    /** The species (tyrant.<mod>.<species>) whose Nursery skin picker order this sets. */
    speciesKey: string;
    /** This mod's skins of the species in picker order (ModDetail.skinOrders), starting skin included. */
    order: string[];
    skins: ModSkinDto[];
    onOpenSkin: (id: string) => void;
    onMakeDefault: (id: string, name: string) => void;
  } = $props();

  /**
   * The order as the list shows it. A move shows at once: live while dragging, then until its save settles, so the list never
   * waits for the save; after that it follows the mod again (and so snaps back if the save failed).
   */
  let shown = $state.raw<string[] | null>(null);
  const listed = $derived(shown ?? order);
  const skinOf = (id: string) => skins.find((s) => s.id === id);
  const nameOf = (id: string) => skinOf(id)?.name ?? id;

  let latest = 0; // the newest save: only its end hands the list back to the mod
  let saving = 0;

  /** Shows the order at once and saves it; each save sends the whole order as shown, so quick repeats stack. */
  function save(next: string[]) {
    shown = next;
    const mine = ++latest;
    saving++;
    void doc.edit('mods.setSkinOrder', { species: speciesKey, order: next }).finally(() => {
      saving--;
      if (mine === latest && !dragging) shown = null;
    });
  }

  function move(id: string, delta: -1 | 1) {
    const next = moveBy(listed, id, delta);
    if (next) save(next);
  }

  function onSkinKey(e: KeyboardEvent, id: string) {
    if (!e.altKey || (e.key !== 'ArrowUp' && e.key !== 'ArrowDown')) return;
    e.preventDefault();
    move(id, e.key === 'ArrowUp' ? -1 : 1);
  }

  // Dragging a skin: pointer events, not HTML drag and drop (the Windows webview takes those for file drops). A press only
  // becomes a drag past a few pixels, so a click still opens the skin. The list moves the skin to where it would land while
  // dragging; letting go keeps it there and saves; Escape or letting go outside the list puts it back.
  const DRAG_START = 5;
  let press: { id: string; x: number; y: number } | null = null;
  let dragging = $state<string | null>(null);
  let pointer = $state({ x: 0, y: 0 });
  let before: { order: string; shown: string[] | null } | null = null; // tells a real move from none, and what to cancel to
  let inside = false;
  let dragged = false; // swallows the click the browser sends after a drag
  let list = $state<HTMLElement>();

  function startPress(e: PointerEvent, id: string) {
    dragged = false;
    if (e.button !== 0) return;
    press = { id, x: e.clientX, y: e.clientY };
    // Keeps the pointer's events coming here even if it is let go outside the window.
    (e.currentTarget as Element).setPointerCapture?.(e.pointerId);
  }

  /** What is under the pointer: outside the list; nothing to drop on (a gap, the dragged skin itself); or a skin (before it, or after it in its lower half). */
  function dropAt(x: number, y: number): { inside: boolean; index?: number } {
    const hit = document.elementFromPoint?.(x, y) ?? null;
    if (!hit || !list?.contains(hit)) return { inside: false };
    const row = hit.closest<HTMLElement>('[data-index]');
    if (!row || row.dataset.skinId === dragging) return { inside: true };
    const index = Number(row.dataset.index);
    const box = row.getBoundingClientRect();
    return { inside: true, index: y > box.top + box.height / 2 ? index + 1 : index };
  }

  function onPointerMove(e: PointerEvent) {
    if (!press) return;
    if (!dragging) {
      if (Math.hypot(e.clientX - press.x, e.clientY - press.y) < DRAG_START) return;
      if (!listed.includes(press.id)) {
        press = null;
        return;
      }
      dragging = press.id;
      before = { order: listed.join('|'), shown };
      shown = listed;
    }
    pointer = { x: e.clientX, y: e.clientY };
    const spot = dropAt(e.clientX, e.clientY);
    inside = spot.inside;
    if (spot.index === undefined) return;
    const next = moveTo(listed, dragging, spot.index);
    if (next) shown = next;
  }

  function onPointerUp() {
    press = null;
    if (!dragging) return;
    dragging = null;
    dragged = true;
    if (!inside || listed.join('|') === before?.order) {
      cancel();
      return;
    }
    before = null;
    save([...listed]);
  }

  /** Back to the order as it was before the drag (with any save still running shown). */
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

  function open(id: string) {
    if (dragged) {
      dragged = false;
      return;
    }
    onOpenSkin(id);
  }
</script>

<svelte:window onpointermove={onPointerMove} onpointerup={onPointerUp} onpointercancel={cancelDrag} onkeydown={onKey} />

<ul class="skin-order" class:dragging={dragging !== null} aria-label="Skin picker order" bind:this={list}>
  {#each listed as id, i (id)}
    {@const skin = skinOf(id)}
    {@const name = nameOf(id)}
    <li data-index={i} data-skin-id={id} class:placeholder={dragging === id}>
      <span class="handle" aria-hidden="true" title="Drag to move" onpointerdown={(e) => startPress(e, id)}>⠿</span>
      <button type="button" class="name" title="Open {name} (drag it, or Alt+↑/↓, to move it)"
        onpointerdown={(e) => startPress(e, id)} onclick={() => open(id)} onkeydown={(e) => onSkinKey(e, id)}>{name}</button>
      {#if skin?.starting}<span class="hint"> · starting skin</span>{:else}
        <button type="button" class="small" aria-label="Make {name} the default" title="Make it the species' starting (default) skin" onclick={() => onMakeDefault(id, name)}>Make default</button>{/if}
      {#if skin?.group}<span class="hint"> · {skin.group}</span>{/if}
      <span class="moves">
        <button type="button" class="small" aria-label="Move {name} up" title="Move up" disabled={i === 0} onclick={() => move(id, -1)}>↑</button>
        <button type="button" class="small" aria-label="Move {name} down" title="Move down" disabled={i === listed.length - 1} onclick={() => move(id, 1)}>↓</button>
      </span>
    </li>
  {/each}
</ul>

{#if dragging}
  <div class="ghost" style:left="{pointer.x + 14}px" style:top="{pointer.y - 12}px" aria-hidden="true">{nameOf(dragging)}</div>
{/if}

<style>
  .skin-order { margin: 0; padding: 0; list-style: none; display: grid; gap: 2px; }
  .skin-order li { display: flex; align-items: center; gap: 4px; padding: 2px 4px; border-radius: 5px; }
  .skin-order li:hover { background: var(--panel-2); }
  .handle { cursor: grab; color: var(--muted); padding: 0 4px; user-select: none; touch-action: none; }
  .name { cursor: pointer; }
  .hint { font-size: 12px; color: var(--muted); }
  .small { margin-left: 4px; padding: 2px 8px; font-size: 0.85em; }
  .moves { margin-left: auto; display: flex; gap: 2px; }
  .skin-order.dragging { cursor: grabbing; user-select: none; }
  .placeholder { outline: 1px dashed var(--accent); outline-offset: -1px; }
  .placeholder > * { opacity: 0.35; }
  .ghost { position: fixed; z-index: 60; pointer-events: none; padding: 5px 10px; border-radius: 5px; white-space: nowrap; background: var(--panel-2); border: 1px solid var(--accent); box-shadow: 0 6px 18px #0008; transform: rotate(-1.5deg); }
</style>
