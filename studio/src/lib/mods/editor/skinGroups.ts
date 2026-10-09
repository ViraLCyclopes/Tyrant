import type { ModSkinDto } from '$lib/rpc/types.gen';

/** One skin's place, as mods.arrangeSkins takes it: the order of the list, and its group (null: none). */
export interface SkinPlace {
  id: string;
  group: string | null;
}

/** A run of skins in the side list: the ungrouped ones (group null) first, then each group. */
export interface SkinSection {
  group: string | null;
  skins: ModSkinDto[];
}

/** The longest group name the core keeps (SkinEntry.MaxGroupLength). */
export const MAX_GROUP = 64;

const same = (a: string | null, b: string | null) => (a === null || b === null ? a === b : a.toLowerCase() === b.toLowerCase());

/** The side list's sections, as the core saves them: ungrouped skins first, then each group in order of its first skin. */
export function sections(skins: readonly ModSkinDto[]): SkinSection[] {
  const list: SkinSection[] = [{ group: null, skins: [] }];
  for (const skin of skins) {
    const group = skin.group ?? null;
    let section = list.find((s) => same(s.group, group));
    if (!section) list.push((section = { group, skins: [] }));
    section.skins.push(skin);
  }
  return list;
}

export function groupNames(skins: readonly ModSkinDto[]): string[] {
  return sections(skins).flatMap((s) => (s.group === null ? [] : [s.group]));
}

/** A typed group name as the core keeps it: trimmed and cut; an existing group's spelling wins whatever the case. Null: none. */
export function groupName(skins: readonly ModSkinDto[], typed: string | null): string | null {
  const name = typed?.trim().slice(0, MAX_GROUP) || null;
  return name === null ? null : (groupNames(skins).find((g) => same(g, name)) ?? name);
}

function flatten(list: readonly SkinSection[]): SkinPlace[] {
  return list.flatMap((s) => s.skins.map((k) => ({ id: k.id, group: s.group })));
}

/** Moves a skin one place up (-1) or down (1) within its section; null when it is already at that end. */
export function moveSkin(skins: readonly ModSkinDto[], id: string, delta: -1 | 1): SkinPlace[] | null {
  const list = sections(skins);
  const section = list.find((s) => s.skins.some((k) => k.id === id));
  if (!section) return null;
  const at = section.skins.findIndex((k) => k.id === id);
  const to = at + delta;
  if (to < 0 || to >= section.skins.length) return null;
  const moved = [...section.skins];
  [moved[at], moved[to]] = [moved[to], moved[at]];
  section.skins = moved;
  return flatten(list);
}

/**
 * A dragged skin dropped into a section (group null: the ungrouped skins) before the skin now at `index` there, or at its end
 * (index = its length). Null when that changes nothing, or the skin or group is not there.
 */
export function moveSkinTo(skins: readonly ModSkinDto[], id: string, group: string | null, index: number): SkinPlace[] | null {
  const list = sections(skins);
  const from = list.find((s) => s.skins.some((k) => k.id === id));
  const to = list.find((s) => same(s.group, group));
  if (!from || !to) return null;
  const at = from.skins.findIndex((k) => k.id === id);
  const skin = from.skins[at];
  let place = Math.max(0, Math.min(index, to.skins.length));
  if (from === to && at < place) place--; // the list closes up where it left
  if (from === to && at === place) return null;
  from.skins = from.skins.filter((k) => k.id !== id);
  to.skins = [...to.skins.slice(0, place), skin, ...to.skins.slice(place)];
  return flatten(list);
}

/** Moves a group past its neighbour (-1 up, 1 down); null at either end (the ungrouped skins always come first). */
export function moveGroup(skins: readonly ModSkinDto[], group: string, delta: -1 | 1): SkinPlace[] | null {
  const list = sections(skins);
  const at = list.findIndex((s) => s.group !== null && same(s.group, group));
  const to = at + delta;
  if (at < 1 || to < 1 || to >= list.length) return null;
  [list[at], list[to]] = [list[to], list[at]];
  return flatten(list);
}

/** Puts skins at the end of a group (made if new), or back at the end of the ungrouped ones (null). */
export function setGroup(skins: readonly ModSkinDto[], ids: readonly string[], typed: string | null): SkinPlace[] {
  const group = groupName(skins, typed);
  const stay = skins.filter((k) => !ids.includes(k.id));
  const go = skins.filter((k) => ids.includes(k.id));
  const placed = [...flatten(sections(stay)), ...go.map((k) => ({ id: k.id, group }))];
  // The core keeps each group together and the ungrouped first: order the same way, so the list does not jump on save.
  const order = sections(placed.map((p) => ({ ...skins.find((k) => k.id === p.id)!, group: p.group })));
  return flatten(order);
}

/** Renames a group; a name matching another group (whatever its case) merges into it. */
export function renameGroup(skins: readonly ModSkinDto[], from: string, typed: string): SkinPlace[] {
  const others = skins.filter((k) => !same(k.group ?? null, from));
  const to = groupName(others, typed) ?? from;
  const renamed = skins.map((k) => ({ ...k, group: same(k.group ?? null, from) ? to : (k.group ?? null) }));
  return flatten(sections(renamed));
}
