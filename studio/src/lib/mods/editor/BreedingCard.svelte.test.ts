import { fireEvent, render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import type { ModAnimalFieldDto } from '$lib/rpc/types.gen';
import BreedingCard from './BreedingCard.svelte';
import { BREEDING_PATHS as B } from './breeding';

const field = (path: string, value: string): ModAnimalFieldDto => ({ path, kind: 'text', value, templateValue: value, editable: true });
const base: Record<string, string> = {
  [B.months]: 'March, April', [B.system]: 'Monogamous', [B.fecundity]: 'Average', [B.gestation]: '6', [B.ideal]: '2', [B.largest]: '4',
  [B.mostEggs]: '0', [B.parenting]: 'MotherOnly', [B.familyCohesion]: '0.9', [B.difficulty]: 'Medium', [B.sitOnEggs]: 'Never',
};

function setupWith(start: Record<string, string>) {
  const values = $state({ ...start });
  const state = { values, valid: true };
  render(BreedingCard, {
    fields: Object.entries(start).map(([p, v]) => field(p, v)), values,
    get valid() { return state.valid; }, set valid(v: boolean) { state.valid = v; },
  });
  return state;
}
const setup = (changes: Partial<Record<keyof typeof B, string>> = {}) =>
  setupWith({ ...base, ...Object.fromEntries(Object.entries(changes).map(([k, v]) => [B[k as keyof typeof B], v])) });

describe('Breeding card', () => {
  it('month_ticks_write_MonthFlags', async () => {
    const { values } = setup();
    await fireEvent.click(screen.getByRole('checkbox', { name: 'July' }));
    expect(values[B.months]).toBe('March, April, July');
  });

  it('unticking_every_month_writes_0_and_warns_never_breeds', async () => {
    const { values } = setup();
    await fireEvent.click(screen.getByRole('checkbox', { name: 'March' }));
    await fireEvent.click(screen.getByRole('checkbox', { name: 'April' }));
    expect(values[B.months]).toBe('0');
    expect(screen.getByText(/never breeds/)).toBeInTheDocument();
  });

  it('All_months_show_ticked', () => {
    setup({ months: 'All' });
    expect(screen.getAllByRole('checkbox').every((c) => (c as HTMLInputElement).checked)).toBe(true);
  });

  it('mating_system_writes_and_shows_description', async () => {
    const { values } = setup();
    await fireEvent.change(screen.getByRole('combobox', { name: 'Mating system' }), { target: { value: 'Promiscuous' } });
    expect(values[B.system]).toBe('Promiscuous');
    expect(screen.getByText(/no lasting pairs/)).toBeInTheDocument();
  });

  it('fertility_shows_the_percent', () => {
    setup();
    expect(screen.getByRole('option', { name: 'Very low (30%)' })).toBeInTheDocument();
  });

  it('fractional_clutch_makes_card_invalid', async () => {
    const state = setup();
    expect(state.valid).toBe(true);
    await fireEvent.input(screen.getByRole('spinbutton', { name: 'Ideal clutch' }), { target: { value: '2.5' } });
    expect(state.values[B.ideal]).toBe('2.5');
    expect(state.valid).toBe(false);
  });

  it('most_eggs_above_16_is_kept_and_refused', async () => {
    const state = setup();
    await fireEvent.input(screen.getByRole('spinbutton', { name: 'Most eggs laid' }), { target: { value: '20' } });
    expect(state.values[B.mostEggs]).toBe('20');
    expect(screen.getByRole('alert')).toHaveTextContent(/at most 16/);
    expect(state.valid).toBe(false);
  });

  it('unknown_parenting_value_is_kept_and_shown', () => {
    setup({ parenting: 'SomethingNew' });
    expect((screen.getByRole('combobox', { name: 'Who raises the babies' }) as HTMLSelectElement).value).toBe('SomethingNew');
  });

  it('father_only_polygynous_warns', () => {
    setup({ parenting: 'FatherOnly', system: 'Polygynous' });
    expect(screen.getByText(/no parent looks after/i)).toBeInTheDocument();
  });

  it('no_breeding_rows_hides_card_and_stays_valid', () => {
    const state = setupWith({});
    expect(screen.queryByRole('region', { name: 'Breeding' })).toBeNull();
    expect(state.valid).toBe(true);
  });
});
