import { fireEvent, screen, waitFor, within } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import type { ModSoundDto } from '$lib/rpc/types.gen';
import { Tab } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import { ModDoc } from './modDoc.svelte';
import SpeciesSoundsCard from './SpeciesSoundsCard.svelte';

const ANAX = 'tyrant.red-spot.anax';
const sound = (over: Partial<ModSoundDto>): ModSoundDto => ({
  event: 'event:/X/Vox/TheroLarge_VoxSocialCall', name: 'Social call', group: 'Calls', species: ANAX, skin: null,
  files: ['sounds/call-1a2b3c4d.ogg'], volume: 0.9, agePitch: 1, chance: null, ...over,
});

async function setup(sounds: ModSoundDto[]) {
  const base = modDetail();
  const detail = modDetail({ skins: [{ ...base.skins[0], id: 'scarred', key: 'red-spot/scarred', species: ANAX, name: 'Scarred' }, base.skins[1]], sounds });
  const rpc = new FakeRpc()
    .on('mods.get', () => detail)
    .on('mods.check', () => ({ errors: [], warnings: [], missingCutouts: [] }))
    .on('mods.removeSound', () => modDetail({ revision: 'r2', sounds: [] }));
  const session = new Session(rpc, new FakePlatform(), memoryStore());
  const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
  tab.key = 'red-spot';
  const doc = new ModDoc('red-spot', rpc, tab, () => {});
  await doc.load();
  const onOpen = vi.fn();
  const onAdd = vi.fn();
  renderWith(SpeciesSoundsCard, session, { doc, species: ANAX, onOpen, onAdd }, tab);
  return { rpc, onOpen, onAdd };
}

describe('SpeciesSoundsCard', () => {
  it("lists the species' own replacements and its skins', with files and volume", async () => {
    await setup([
      sound({}),
      sound({ event: 'event:/X/Growl', name: 'Growl', species: null, skin: 'red-spot/scarred', files: ['sounds/g1.ogg', 'sounds/g2.ogg'], volume: 1 }),
      sound({ event: 'event:/X/Other', name: 'Other species', species: 'Carcharodontosaurus' }),
      sound({ event: 'event:/X/All', name: 'For everyone', species: null }),
    ]);

    const rows = within(screen.getByRole('list', { name: 'Sound replacements' })).getAllByRole('listitem');
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveTextContent('Social call');
    expect(rows[0]).toHaveTextContent('call-1a2b3c4d.ogg');
    expect(rows[0]).toHaveTextContent('volume 0.90');
    expect(rows[1]).toHaveTextContent('Growl');
    expect(rows[1]).toHaveTextContent('Scarred skin');
    expect(rows[1]).toHaveTextContent('g1.ogg, g2.ogg');
  });

  it('opens a replacement to edit it, removes one, and adds one for this species', async () => {
    const { rpc, onOpen, onAdd } = await setup([sound({})]);

    await fireEvent.click(screen.getByRole('button', { name: 'Edit Social call' }));
    expect(onOpen).toHaveBeenCalledWith({ event: 'event:/X/Vox/TheroLarge_VoxSocialCall', species: ANAX, skin: null });
    await fireEvent.click(screen.getByRole('button', { name: 'Add a sound for this species' }));
    expect(onAdd).toHaveBeenCalled();
    await fireEvent.click(screen.getByRole('button', { name: 'Remove Social call' }));
    await waitFor(() => expect(rpc.callsTo('mods.removeSound')[0]?.params).toMatchObject({ event: 'event:/X/Vox/TheroLarge_VoxSocialCall', species: ANAX, skin: null }));
  });

  it('says when the species has no sounds of its own yet', async () => {
    await setup([sound({ species: 'Carcharodontosaurus' })]);

    expect(screen.getByText(/plays its template's sounds/)).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Sound replacements' })).toBeNull();
  });
});
