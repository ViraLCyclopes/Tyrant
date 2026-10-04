import type { ModCheckReport } from '$lib/rpc/types.gen';

/** The order mod.json and the game use (SkinSlotNames.All in Tyrant.Framework.Core). */
export const SLOT_ORDER = ['diffuse', 'normal', 'extra', 'pattern', 'fur', 'infantDiffuse', 'infantNormal', 'infantExtra', 'infantPattern', 'infantFur'];

export const SLOT_LABELS: Record<string, string> = {
  diffuse: 'diffuse',
  normal: 'normal',
  extra: 'extra',
  pattern: 'pattern',
  fur: 'fur',
  infantDiffuse: 'infant diffuse',
  infantNormal: 'infant normal',
  infantExtra: 'infant extra',
  infantPattern: 'infant pattern',
  infantFur: 'infant fur',
};

/** The slots to show: the base skin's (when the game data is known) plus any the skin has that the base lacks. */
export function slotsFor(own: Record<string, string> | null, base: string[] | null): string[] {
  const wanted = new Set([...(base ?? ['diffuse', 'normal', 'extra', 'pattern']), ...Object.keys(own ?? {})]);
  return SLOT_ORDER.filter((s) => wanted.has(s));
}

export function isColourSlot(slot: string): boolean {
  return slot === 'diffuse' || slot === 'infantDiffuse';
}

/** Check lines about one skin start with its key ("red-spot/blue: …" or "red-spot/blue male diffuse: …"). */
export function checkLinesFor(report: ModCheckReport | null, skinKey: string): { errors: string[]; warnings: string[] } {
  const mine = (line: string) => line.startsWith(`${skinKey}:`) || line.startsWith(`${skinKey} `);
  return { errors: (report?.errors ?? []).filter(mine), warnings: (report?.warnings ?? []).filter(mine) };
}
