import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { tick } from 'svelte';
import { describe, expect, it, vi } from 'vitest';
import type { ModAnimalFieldDto, ModBehaviourProfileDto, ModBehaviourRuleDto, ModSpeciesBehaviourDto } from '$lib/rpc/types.gen';
import BehaviourCard from './BehaviourCard.svelte';

const field = (path: string, value: string): ModAnimalFieldDto => ({ path, kind: 'text', value, templateValue: value, editable: true });
const P = '/brain/combat/shared/combatProfile/';
const C = '/brain/combat/';
const base: Record<string, string> = {
  [P + 'confidenceLevel']: 'Confident', [P + 'temperament']: 'Aggressive', [P + 'cannibalism']: 'None',
  [C + 'adult/lethalityLevel']: 'High', [C + 'adult/defenseLevel']: 'Average', [C + 'adult/cooperationLevel']: 'Individualistic',
  [C + 'infant/lethalityLevel']: 'Low', [C + 'infant/defenseLevel']: 'Low', [C + 'infant/cooperationLevel']: 'Individualistic',
  [C + 'shared/aggressionRangeMult']: '1', [C + 'shared/cannibalismPropensity']: '0.5', [C + 'shared/canAttackWhileSwimming']: 'false',
  '/clade': 'Theropoda',
};
const profiles: ModBehaviourProfileDto[] = [
  { asset: 'Tank', name: 'Tank', confidence: 'Confident', temperament: 'Aggressive', cannibalism: 'None', rules: ['Hunt'], usedBy: ['Rex', 'Giga'] },
  { asset: 'Prey', name: 'Prey', confidence: 'Skittish', temperament: 'Passive', cannibalism: 'WhenStarving', rules: ['Flee'], usedBy: ['Dryo'] },
];
const rules: ModBehaviourRuleDto[] = [
  { asset: 'Hunt', name: 'Hunts prey', shownInGame: true },
  { asset: 'Flee', name: 'Flees', shownInGame: false },
];

function setup(o: { start?: Record<string, string>; profs?: ModBehaviourProfileDto[]; behaviour?: ModSpeciesBehaviourDto | null; answer?: boolean; template?: string | null } = {}) {
  const start = o.start ?? base;
  const values = $state({ ...start });
  let behaviour = o.behaviour ?? null;
  const confirm = vi.fn(async (_message: string) => o.answer ?? true);
  render(BehaviourCard, {
    fields: Object.entries(start).map(([p, v]) => field(p, v)), values, profiles: o.profs ?? profiles, rules,
    templateProfile: o.template === undefined ? 'Tank' : o.template, confirm,
    get behaviour() { return behaviour; }, set behaviour(v: ModSpeciesBehaviourDto | null) { behaviour = v; },
  });
  return { values, confirm, get behaviour() { return behaviour; } };
}
const box = (name: string) => screen.getByRole('checkbox', { name: new RegExp(`^${name}`) }) as HTMLInputElement;

describe('Behaviour card', () => {
  it('defaults_to_the_template_profile_and_its_rules', () => {
    const s = setup();
    expect((screen.getByRole('combobox', { name: 'Start from' }) as HTMLSelectElement).value).toBe('Tank');
    expect(screen.getByRole('option', { name: 'Tank (used by Rex, Giga)' })).toBeInTheDocument();
    expect(box('Hunts prey').checked).toBe(true);
    expect(box('Flees').checked).toBe(false);
    expect(s.behaviour).toBeNull();
  });

  it('switching_profile_asks_then_replaces_ticks_and_the_three_values', async () => {
    const s = setup({ start: { ...base, [P + 'temperament']: 'Defensive' } });
    await fireEvent.change(screen.getByRole('combobox', { name: 'Start from' }), { target: { value: 'Prey' } });
    expect(s.confirm).toHaveBeenCalledWith('Start from Prey? This replaces your rule ticks and the confidence, temperament and cannibalism choices.');
    await waitFor(() => expect(s.behaviour).toEqual({ startFrom: 'Prey', rules: ['Flee'] }));
    expect(box('Hunts prey').checked).toBe(false);
    expect(box('Flees').checked).toBe(true);
    expect([s.values[P + 'confidenceLevel'], s.values[P + 'temperament'], s.values[P + 'cannibalism']]).toEqual(['Skittish', 'Passive', 'WhenStarving']);
  });

  it('declining_keeps_everything', async () => {
    const s = setup({ start: { ...base, [P + 'temperament']: 'Defensive' }, answer: false });
    const select = screen.getByRole('combobox', { name: 'Start from' }) as HTMLSelectElement;
    await fireEvent.change(select, { target: { value: 'Prey' } });
    await waitFor(() => expect(s.confirm).toHaveBeenCalled());
    await tick();
    expect(select.value).toBe('Tank');
    expect(s.behaviour).toBeNull();
    expect(box('Hunts prey').checked).toBe(true);
    expect(s.values[P + 'temperament']).toBe('Defensive');
  });

  it('switching_without_changes_does_not_ask', async () => {
    const s = setup();
    await fireEvent.change(screen.getByRole('combobox', { name: 'Start from' }), { target: { value: 'Prey' } });
    await waitFor(() => expect(s.behaviour?.startFrom).toBe('Prey'));
    expect(s.confirm).not.toHaveBeenCalled();
  });

  it('ticking_Hunts_prey_sets_behaviour_rules', async () => {
    const s = setup({ behaviour: { startFrom: 'Prey', rules: [] }, template: 'Prey' });
    await fireEvent.click(box('Hunts prey'));
    expect(s.behaviour).toEqual({ startFrom: 'Prey', rules: ['Hunt'] });
  });

  it('empty_catalogue_shows_the_dump_hint_and_keeps_fighting_fields', () => {
    setup({ profs: [], template: null });
    expect(screen.getByText('Behaviour profiles need a game data dump. Run Dump in Game first.')).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Start from' })).toBeNull();
    expect(screen.queryByRole('checkbox', { name: /Hunts prey/ })).toBeNull();
    expect(screen.getByRole('combobox', { name: 'Adult lethality' })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Clade' })).toBeInTheDocument();
  });

  it('writes_fighting_fields_and_clade_to_their_paths', async () => {
    const s = setup();
    await fireEvent.change(screen.getByRole('combobox', { name: 'Baby cooperation' }), { target: { value: 'Devoted' } });
    await fireEvent.input(screen.getByRole('spinbutton', { name: 'Attack range ×' }), { target: { value: '1.5' } });
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Can fight while swimming' }));
    await fireEvent.change(screen.getByRole('combobox', { name: 'Clade' }), { target: { value: 'Arthropoda' } });
    expect([s.values[C + 'infant/cooperationLevel'], s.values[C + 'shared/aggressionRangeMult'], s.values[C + 'shared/canAttackWhileSwimming'], s.values['/clade']])
      .toEqual(['Devoted', '1.5', 'true', 'Arthropoda']);
  });

  it('a_missing_path_hides_its_control', () => {
    const { [C + 'infant/lethalityLevel']: _a, '/clade': _b, ...rest } = base;
    setup({ start: rest });
    expect(screen.queryByRole('combobox', { name: 'Baby lethality' })).toBeNull();
    expect(screen.queryByRole('combobox', { name: 'Clade' })).toBeNull();
    expect(screen.getByRole('combobox', { name: 'Adult lethality' })).toBeInTheDocument();
  });
});
