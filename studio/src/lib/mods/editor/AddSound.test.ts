import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import { Session } from '$lib/stores/session.svelte';
import AddSound from './AddSound.svelte';

const call = { event: 'event:/X/Vox/TheroLarge_VoxSocialCall', name: 'Social call', group: 'Calls', species: ['Carcharodontosaurus'], lengthMs: null, oneShot: true, perAnimal: true };
const click = { event: 'event:/User Interface/UI_Click', name: 'Click', group: 'Interface', species: [], lengthMs: 90, oneShot: true, perAnimal: false };

function setup() {
  const platform = new FakePlatform();
  platform.fileLists.push(['D:\\sounds\\call.ogg']);
  const rpc = new FakeRpc()
    .on('mods.species', () => ({ hasDump: true, species: [{ speciesId: 'Carcharodontosaurus', vivarium: false, skins: [] }] }))
    .on('sounds.forSpecies', () => ({ speciesId: 'Carcharodontosaurus', hasEventList: true, sounds: [call] }))
    .on('sounds.search', () => ({ hasEventList: true, sounds: [click] }))
    .on('mods.replaceSound', () => modDetail({ id: 'carch-voice' }));
  const session = new Session(rpc, platform, memoryStore());
  const onDone = vi.fn();
  renderWith(AddSound, session, { modId: 'carch-voice', onDone });
  return { rpc, onDone };
}

describe('AddSound', () => {
  it("replaces one of a species' sounds in this mod and opens it", async () => {
    const { rpc, onDone } = setup();

    await fireEvent.change(await screen.findByRole('combobox', { name: 'Sounds of' }), { target: { value: 'Carcharodontosaurus' } });
    await fireEvent.click(await screen.findByRole('button', { name: 'Replace Social call' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Choose files…' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Add to mod' }));

    await waitFor(() => expect(rpc.callsTo('mods.replaceSound')[0]?.params).toMatchObject({ id: 'carch-voice', event: call.event, species: 'Carcharodontosaurus' }));
    await waitFor(() => expect(onDone).toHaveBeenCalledWith({ event: call.event, species: 'Carcharodontosaurus', skin: null }));
  });

  it('can pick from every game sound', async () => {
    setup();

    await fireEvent.change(await screen.findByRole('combobox', { name: 'Sounds of' }), { target: { value: '__all__' } });

    expect(await screen.findByRole('searchbox', { name: 'Find a sound' })).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Replace Click' })).toBeInTheDocument();
  });
});
