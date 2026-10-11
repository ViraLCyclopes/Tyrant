import type { ModPortraitDto } from '$lib/rpc/types.gen';
import type { Variant } from './colors';

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
/** The Normal column's slot for each row. */
export const RENDER_SLOT: Record<RenderSex, string> = { male: 'adultMale', female: 'adultFemale', infant: 'infant' };
/** Each kind of animal's column suffix. */
const VARIANT_SUFFIX: Record<Variant, string> = { normal: '', albino: 'Albino', melanistic: 'Melanistic', leucistic: 'Leucistic' };

export type RenderShot = { sex: RenderSex; variant: Variant; slot: string };

/**
 * The pictures a render writes, row by row (one set of maps per row): every column asked for whose slot is empty or was
 * rendered before; a hand-picked one only when replace is ticked.
 */
export function renderShots(shown: readonly ModPortraitDto[], sexes: readonly RenderSex[], variants: readonly Variant[], replace: boolean): RenderShot[] {
  return sexes.flatMap((sex) =>
    variants
      .map((variant) => ({ sex, variant, slot: RENDER_SLOT[sex] + VARIANT_SUFFIX[variant] }))
      .filter(({ slot }) => {
        const current = shown.find((p) => p.slot === slot);
        return replace || !current?.own || current.rendered;
      }),
  );
}

/** The game shows portraits at 2:1 (128×64); within 2% is fine. */
export function isTwoToOne(width: number, height: number): boolean {
  return height > 0 && Math.abs(width / height - 2) <= 0.04;
}
