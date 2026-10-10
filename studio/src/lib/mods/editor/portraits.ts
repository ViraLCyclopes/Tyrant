import type { ModPortraitDto } from '$lib/rpc/types.gen';

/** The game's 12 portrait slots (AnimalPortraits' fields), row by row as the card shows them. */
export const PORTRAIT_ROWS = ['Adult male', 'Adult female', 'Infant'] as const;
export const PORTRAIT_COLUMNS = ['Normal', 'Albino', 'Melanistic', 'Leucistic'] as const;
const ROW_KEYS = ['adultMale', 'adultFemale', 'infant'];
const COLUMN_KEYS = ['', 'Albino', 'Melanistic', 'Leucistic'];
export const PORTRAIT_SLOTS = ROW_KEYS.flatMap((r) => COLUMN_KEYS.map((c) => r + c));

export function slotOf(row: number, column: number): string {
  return ROW_KEYS[row] + COLUMN_KEYS[column];
}

export type RenderSex = 'male' | 'female' | 'infant';
/** The Normal column, which Render from 3D fills. */
export const RENDER_SLOT: Record<RenderSex, string> = { male: 'adultMale', female: 'adultFemale', infant: 'infant' };

/** The sexes a render writes: a slot that is empty or was rendered before; a hand-picked one only when replace is ticked. */
export function renderTargets(shown: readonly ModPortraitDto[], sexes: readonly RenderSex[], replace: boolean): RenderSex[] {
  return sexes.filter((sex) => {
    const current = shown.find((p) => p.slot === RENDER_SLOT[sex]);
    return replace || !current?.own || current.rendered;
  });
}

/** The game shows portraits at 2:1 (128×64); within 2% is fine. */
export function isTwoToOne(width: number, height: number): boolean {
  return height > 0 && Math.abs(width / height - 2) <= 0.04;
}
