import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import type { ModAnimalFieldDto } from '$lib/rpc/types.gen';
import SocialCard from './SocialCard.svelte';

const s = '/brain/social';
const P = {
  malesMin: `${s}/adult/socialMaleCountRange/x`, malesMax: `${s}/adult/socialMaleCountRange/y`,
  femalesMin: `${s}/adult/socialFemaleCountRange/x`, femalesMax: `${s}/adult/socialFemaleCountRange/y`,
  spaceMale: `${s}/shared/individualRangeMale`, spaceFemale: `${s}/shared/individualRangeFemale`,
  overlap: `${s}/shared/rangeOverlapAllowance`, cohesion: `${s}/adult/socialPositionalCohesion`, leader: `${s}/adult/socialLeaderType`,
} as const;
const base: Record<keyof typeof P, string> = {
  malesMin: '1', malesMax: '2', femalesMin: '2', femalesMax: '4', spaceMale: '1000', spaceFemale: '500', overlap: '0.5', cohesion: '0.5', leader: 'Matriarch',
};
const field = (path: string, value: string): ModAnimalFieldDto => ({ path, kind: path === P.leader ? 'text' : 'number', value, templateValue: value, editable: true });

function setup(changes: Partial<Record<keyof typeof P, string>> = {}, props: { ownSocialInfo?: boolean } = {}) {
  const start = Object.fromEntries(Object.entries({ ...base, ...changes }).map(([k, v]) => [P[k as keyof typeof P], v]));
  const values = $state({ ...start });
  render(SocialCard, { fields: Object.entries(start).map(([p, v]) => field(p, v)), values, ...props });
  return values;
}
const preview = () => within(screen.getByRole('region', { name: 'What the game will show' }));
const line = (name: RegExp) => preview().getByText(name).parentElement!;

describe('Social card', () => {
  it('crowding_label_replaces_range_overlap', () => {
    setup();
    expect(screen.getByRole('spinbutton', { name: 'Crowding allowed (0–1)' })).toBeInTheDocument();
    expect(screen.queryByText(/Range overlap/)).toBeNull();
  });

  it('preview_starts_at_fewest_and_shows_exhibit_size', () => {
    setup();
    expect((preview().getByRole('spinbutton', { name: 'Males' }) as HTMLInputElement).value).toBe('1');
    expect((preview().getByRole('spinbutton', { name: 'Females' }) as HTMLInputElement).value).toBe('2');
    expect(line(/Expected exhibit size/)).toHaveTextContent('1750'); // 1000×lerp(1,1,0.5)=1000 + 500×lerp(2,1,0.5)=750
    expect(line(/Expected social groups/)).toHaveTextContent('1');
    expect(line(/Expected nomads/)).toHaveTextContent('0');
    expect(line(/Group cohesion/)).toHaveTextContent('Medium');
    expect(line(/Herd leader/)).toHaveTextContent('Matriarch');
    expect(preview().getByText(/mixed groups/i)).toBeInTheDocument();
  });

  it('preview_follows_unsaved_edits_and_does_not_write_values', async () => {
    const values = setup();
    await fireEvent.input(screen.getByRole('spinbutton', { name: 'Space per male (m²)' }), { target: { value: '2000' } });
    expect(line(/Expected exhibit size/)).toHaveTextContent('2750');
    const before = JSON.stringify(values);
    await fireEvent.input(preview().getByRole('spinbutton', { name: 'Males' }), { target: { value: '2' } });
    expect(line(/Expected exhibit size/)).toHaveTextContent('3750'); // 2000×lerp(2,1,.5)=3000 + 750
    expect(JSON.stringify(values)).toBe(before);
  });

  it('blank_space_shows_dash_not_NaN', async () => {
    setup();
    await fireEvent.input(screen.getByRole('spinbutton', { name: 'Space per male (m²)' }), { target: { value: '' } });
    expect(screen.queryByText(/NaN/)).toBeNull();
    expect(line(/Expected exhibit size/)).toHaveTextContent('—');
  });

  it('no_herd_shows_None_for_cohesion_and_leader', () => {
    setup({ malesMax: '1', femalesMin: '0', femalesMax: '0' });
    expect(line(/Group cohesion/)).toHaveTextContent('None');
    expect(line(/Herd leader/)).toHaveTextContent('None');
  });

  it('own_social_info_replaces_group_text', () => {
    setup({}, { ownSocialInfo: true });
    expect(preview().getByText(/own social info/i)).toBeInTheDocument();
    expect(preview().queryByText(/mixed groups/i)).toBeNull();
  });
});
