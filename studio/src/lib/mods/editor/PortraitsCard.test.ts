import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { Tab } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import { ModDoc } from './modDoc.svelte';
import PortraitsCard from './PortraitsCard.svelte';
import { PORTRAIT_SLOTS } from './portraits';

const rows = PORTRAIT_SLOTS.map((slot) => {
  if (slot === 'adultMale') return { slot, file: 'portraits/adultMale.png', own: true, rendered: false, width: 128, height: 64 };
  if (slot === 'adultFemale') return { slot, file: 'portraits/adultFemale.png', own: true, rendered: false, width: 64, height: 64 };
  if (slot === 'infant') return { slot, file: 'game/infant.png', own: false, rendered: false, width: 128, height: 64 };
  return { slot, file: null, own: false, rendered: false, width: 0, height: 0 };
});

async function setup(detail = modDetail(), species = false) {
  const rpc = new FakeRpc()
    .on('mods.get', () => detail)
    .on('mods.check', () => ({ errors: [], warnings: [], missingCutouts: [] }))
    .on('mods.portraits', () => ({ portraits: rows }));
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, memoryStore());
  const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
  tab.key = 'red-spot';
  const doc = new ModDoc('red-spot', rpc, tab, () => {});
  await doc.load();
  const target = species ? { speciesId: 'example-animal', speciesKey: 'tyrant.red-spot.example-animal' } : { skin: doc.detail!.skins[0] };
  renderWith(PortraitsCard, session, { doc, target }, tab);
  return { rpc, platform, doc };
}

describe('PortraitsCard', () => {
  it('shows own, game and missing portraits, warns on a non 2:1 picture, and sets and clears slots', async () => {
    const { rpc, platform } = await setup();
    platform.files.push('D:\art\male.png');
    rpc.on('mods.setPortraits', () => modDetail({ revision: 'r2' }));

    expect(await screen.findByRole('img', { name: 'Adult male, Normal (this mod)' })).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Infant, Normal (game)' })).toBeInTheDocument();
    expect(screen.getAllByText('Game portrait').length).toBeGreaterThan(0);
    expect(screen.getByText(/shows portraits at 2:1/)).toBeInTheDocument();

    await fireEvent.click(screen.getByRole('button', { name: 'Choose PNG for Adult male, Albino' }));
    await waitFor(() => expect(rpc.callsTo('mods.setPortraits')[0]?.params).toMatchObject({ skin: 'blue', set: { adultMaleAlbino: { png: 'D:\art\male.png' } } }));

    await fireEvent.click(screen.getByRole('button', { name: 'Clear Adult male, Normal' }));
    await waitFor(() => expect(rpc.callsTo('mods.setPortraits')[1]?.params).toMatchObject({ set: { adultMale: null } }));
    expect(screen.getByRole('button', { name: 'Clear Infant, Normal' })).toBeDisabled(); // the game's: nothing to clear
  });

  it('notes the row the game never shows on a male-only skin', async () => {
    const detail = modDetail();
    detail.skins[0] = { ...detail.skins[0], female: null };
    await setup(detail);
    expect(await screen.findByText('The game never shows this row for this skin.')).toBeInTheDocument();
  });

  it('on the species page writes through mods.setSpeciesPortraits without undo', async () => {
    const { rpc, platform, doc } = await setup(modDetail(), true);
    platform.files.push('D:\art\male.png');
    rpc.on('mods.setSpeciesPortraits', () => modDetail({ revision: 'r2' }));

    await fireEvent.click(await screen.findByRole('button', { name: 'Choose PNG for Adult male, Normal' }));
    await waitFor(() => expect(rpc.callsTo('mods.setSpeciesPortraits')[0]?.params).toMatchObject({ speciesId: 'example-animal', set: { adultMale: { png: 'D:\art\male.png' } } }));
    expect(doc.canUndo).toBe(false);
  });
});
