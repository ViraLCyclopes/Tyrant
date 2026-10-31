import { fireEvent, render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import type { ModAnimalFieldDto } from '$lib/rpc/types.gen';
import HabitatCard from './HabitatCard.svelte';

const field = (path: string, value: string): ModAnimalFieldDto => ({ path, kind: 'text', value, templateValue: value, editable: true });
const base: Record<string, string> = {
  '/MinFenceHeight': 'Height2_5M', '/MinFenceStrength': 'High', '/waterAffinity': 'MediumDislike', '/minimumWaterDepth': '0',
  '/treeBiomes': 'Coastal, Wetland', '/nurseryBackgroundBiome': 'Wetland',
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
    const select = screen.getByRole('combobox', { name: 'Water liking' }) as HTMLSelectElement;
    expect(select.value).toBe('MediumDislike');
    expect(screen.getByRole('option', { name: 'Dislikes water' })).toBeInTheDocument();
    await fireEvent.change(select, { target: { value: 'StrongLike' } });
    expect(values['/waterAffinity']).toBe('StrongLike');
  });

  it('a_missing_waterAffinity_row_hides_Water_liking', () => {
    const { '/waterAffinity': _w, ...rest } = base;
    setup(rest);
    expect(screen.queryByRole('combobox', { name: 'Water liking' })).toBeNull();
    expect(screen.getByRole('combobox', { name: 'Fence height' })).toBeInTheDocument();
  });

  it('depth_writes_and_pointer_shows', async () => {
    const values = setup();
    await fireEvent.input(screen.getByRole('spinbutton', { name: /Minimum water depth/ }), { target: { value: '1.5' } });
    expect(values['/minimumWaterDepth']).toBe('1.5');
    expect(screen.getByText("Only the Paleopedia shows this; it doesn't change behaviour.")).toBeInTheDocument();
    expect(screen.getByText('Plant coverage, water, temperature and bed are in Animal stats. Space per animal is on the Social card.')).toBeInTheDocument();
  });
  it('biome_ticks_write_flags_in_enum_order', async () => {
    const values = setup();
    expect((screen.getByRole('checkbox', { name: 'Coastal' }) as HTMLInputElement).checked).toBe(true);
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Boreal' }));
    expect(values['/treeBiomes']).toBe('Boreal, Coastal, Wetland');
  });

  it('unticking_the_last_biome_writes_0_and_warns', async () => {
    const values = setup({ ...base, '/treeBiomes': 'Desert' });
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Desert' }));
    expect(values['/treeBiomes']).toBe('0');
    expect(screen.getByText(/herbivores never browse/i)).toBeInTheDocument();
  });

  it('All_shows_every_biome_ticked', async () => {
    const values = setup({ ...base, '/treeBiomes': 'All' });
    expect(screen.getAllByRole('checkbox').every((c) => (c as HTMLInputElement).checked)).toBe(true);
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Desert' }));
    expect(values['/treeBiomes']).toBe('Boreal, Coastal, Temperate, Scrubland, Wetland, Tropical, Grassland');
  });

  it('nursery_background_select_writes_Desert', async () => {
    const values = setup();
    await fireEvent.change(screen.getByRole('combobox', { name: 'Nursery background' }), { target: { value: 'Desert' } });
    expect(values['/nurseryBackgroundBiome']).toBe('Desert');
  });
});
