/**
 * A custom species' skins in its Nursery skin picker order (mods.setSkinOrder): moved by dragging on the species page, or one
 * place at a time. Each returns the new order, or null when nothing changes.
 */

/** Moves a skin before the one now at `index`, or to the end (index = the length). Null when that changes nothing or it is not there. */
export function moveTo(order: readonly string[], id: string, index: number): string[] | null {
  const at = order.indexOf(id);
  if (at < 0) return null;
  let place = Math.max(0, Math.min(index, order.length));
  if (at < place) place--; // the list closes up where it left
  if (at === place) return null;
  const rest = order.filter((k) => k !== id);
  return [...rest.slice(0, place), id, ...rest.slice(place)];
}

/** Moves a skin one place up (-1) or down (1); null when it is already at that end, or not there. */
export function moveBy(order: readonly string[], id: string, delta: -1 | 1): string[] | null {
  const at = order.indexOf(id);
  const to = at + delta;
  if (at < 0 || to < 0 || to >= order.length) return null;
  const moved = [...order];
  [moved[at], moved[to]] = [moved[to], moved[at]];
  return moved;
}
