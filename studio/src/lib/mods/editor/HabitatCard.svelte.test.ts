import { fireEvent, render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import type { ModAnimalFieldDto } from '$lib/rpc/types.gen';
import HabitatCard from './HabitatCard.svelte';

const field = (path: string, value: string): ModAnimalFieldDto => ({ path, kind: 'text', value, templateValue: value, editable: true });
const base: Record<string, string> = {
  '/MinFenceHeight': 'Height2_5M', '/MinFenceStrength': 'High', '/waterAffinity': 'MediumDislike', '/minimumWaterDepth': '0',
};

function setup(start: Record<string, string> = base) {
  const values = $state({ ...start });
  render(HabitatCard, { fields: Object.entries(start).map(([p, v]) => field(p, v)), values });
  return values;
}

describe('Habitat card', () => {
  it('fence_height_select_writes_Height4M', async () => {
    const values = setup();
    const select = screen.getByRole('combobox', { name: 'Fence height' }) as HTMLSelectElement;
    expect(select.value).toBe('Height2_5M');
    await fireEvent.change(select, { target: { value: 'Height4M' } });
    expect(values['/MinFenceHeight']).toBe('Height4M');
    expect(screen.getByRole('option', { name: '4 m' })).toBeInTheDocument();
  });

  it('water_liking_writes_StrongLike', async () => {
    const values = setup();
    const select = screen.getByRole('combobox', { name: 'Water' }) as HTMLSelectElement;
    expect(select.value).toBe('MediumDislike');
    expect(screen.getByRole('option', { name: 'Dislikes water' })).toBeInTheDocument();
    await fireEvent.change(select, { target: { value: 'StrongLike' } });
    expect(values['/waterAffinity']).toBe('StrongLike');
  });

  it('a_missing_waterAffinity_row_hides_Water', () => {
    const { '/waterAffinity': _w, ...rest } = base;
    setup(rest);
    expect(screen.queryByRole('combobox', { name: 'Water' })).toBeNull();
    expect(screen.getByRole('combobox', { name: 'Fence height' })).toBeInTheDocument();
  });

  it('depth_writes_and_pointer_shows', async () => {
    const values = setup();
    await fireEvent.input(screen.getByRole('spinbutton', { name: /Minimum water depth/ }), { target: { value: '1.5' } });
    expect(values['/minimumWaterDepth']).toBe('1.5');
    expect(screen.getByText("Only the Paleopedia shows this; it doesn't change behaviour.")).toBeInTheDocument();
    expect(screen.getByText('Plant coverage, water, temperature and bed are in Animal stats. Space per animal is on the Social card.')).toBeInTheDocument();
  });
});
