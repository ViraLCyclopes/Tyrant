import { fireEvent, render, screen } from '@testing-library/svelte';
import { tick } from 'svelte';
import { describe, expect, it } from 'vitest';
import type { ModAnimalFieldDto } from '$lib/rpc/types.gen';
import DietCard from './DietCard.svelte';

const field = (path: string, value: string): ModAnimalFieldDto => ({ path, kind: 'text', value, templateValue: value, editable: true });
const base = { '/diet': 'Herbivore', '/niches/infantNiche': 'Small, Grazer', '/niches/adolescentNiche': 'Medium, Grazer', '/niches/adultNiche': 'Medium, Grazer' };

function setup(start: Record<string, string> = base, hasPredation = true) {
  const list = Object.entries(start).map(([p, v]) => field(p, v));
  const values = $state({ ...start });
  const { component } = render(DietCard, { fields: list, values, hasPredation });
  return { values, component };
}

describe('Diet and niche card', () => {
  it('ticking Meat writes "Herbivore, Carnivore"', async () => {
    const { values } = setup();
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Meat' }));
    expect(values['/diet']).toBe('Herbivore, Carnivore');
  });

  it('Kibble is kept when Plants is unticked', async () => {
    const { values } = setup({ ...base, '/diet': 'Herbivore, Kibble' });
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Plants' }));
    expect(values['/diet']).toBe('Kibble');
  });

  it('choosing Large replaces Medium', async () => {
    const { values } = setup({ ...base, '/niches/adultNiche': 'Medium, Weird, Grazer' });
    await fireEvent.change(screen.getByRole('combobox', { name: 'Adult size' }), { target: { value: 'Large' } });
    expect(values['/niches/adultNiche']).toBe('Large, Grazer, Weird');
  });

  it('ticking a role writes it in enum order', async () => {
    const { values } = setup();
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Adult: Browser' }));
    expect(values['/niches/adultNiche']).toBe('Medium, Grazer, Browser');
  });

  it('no food makes valid false', async () => {
    const list = Object.entries({ ...base, '/diet': '' }).map(([p, v]) => field(p, v));
    let valid = true;
    const values = $state({ ...base, '/diet': 'Herbivore' });
    render(DietCard, { fields: list, values, hasPredation: true, get valid() { return valid; }, set valid(v: boolean) { valid = v; } });
    await tick();
    expect(valid).toBe(true);
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Plants' }));
    await tick();
    expect(screen.getByText('Tick at least one food.')).toBeInTheDocument();
    expect(valid).toBe(false);
  });

  it('shows a warning for Predator without a hunts-prey rule', () => {
    setup({ ...base, '/diet': 'Carnivore', '/niches/adultNiche': 'Large, Predator' }, false);
    expect(screen.getByText(/no "Hunts prey" rule/)).toBeInTheDocument();
  });

  it('a missing /niches/infantNiche row hides the Baby column', () => {
    const { '/niches/infantNiche': _gone, ...rest } = base;
    setup(rest);
    expect(screen.queryByRole('combobox', { name: 'Baby size' })).toBeNull();
    expect(screen.getByRole('combobox', { name: 'Adult size' })).toBeInTheDocument();
  });
});
