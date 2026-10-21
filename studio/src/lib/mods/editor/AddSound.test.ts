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

const ANAX = 'tyrant.carch-voice.anax';
const customRow = (speciesId: string, name: string, own: boolean) => ({
  speciesId, name, modId: speciesId.split('.')[1], modName: 'Mod', own, inWorkspace: true, installed: false, enabled: false,
  skins: [{ index: 0, name: 'Default', male: true, female: true }],
});

function setup(props: Record<string, unknown> = {}) {
  const platform = new FakePlatform();
  platform.fileLists.push(['D:\\sounds\\call.ogg']);
  const rpc = new FakeRpc()
    .on('mods.species', () => ({ hasDump: true, species: [{ speciesId: 'Carcharodontosaurus', vivarium: false, skins: [] }] }))
    .on('mods.customSpecies', () => ({ species: [customRow(ANAX, 'Anax', true), customRow('tyrant.mod-a.rex', 'Rex', false)] }))
    .on('sounds.forSpecies', (p) =>
      (p as { species: string }).species === ANAX
        ? { speciesId: ANAX, template: 'Carcharodontosaurus', hasEventList: true, sounds: [call] }
        : { speciesId: 'Carcharodontosaurus', hasEventList: true, sounds: [call] })
    .on('sounds.search', () => ({ hasEventList: true, sounds: [click] }))
    .on('mods.replaceSound', () => modDetail({ id: 'carch-voice' }));
  const session = new Session(rpc, platform, memoryStore());
  const onDone = vi.fn();
  renderWith(AddSound, session, { modId: 'carch-voice', onDone, ...props });
  return { rpc, onDone, platform };
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

  it("lists this mod's own species by name and replaces a call for that species only", async () => {
    const { rpc, onDone } = setup();
    const select = await screen.findByRole('combobox', { name: 'Sounds of' });
    await screen.findByRole('option', { name: 'Anax' });

    expect([...select.querySelectorAll('optgroup')].map((g) => g.label)).toEqual(['Base game', "This mod's species"]);
    expect(screen.queryByRole('option', { name: /Rex/ })).not.toBeInTheDocument();
    await fireEvent.change(select, { target: { value: ANAX } });
    expect(await screen.findByText(/Anax plays Carcharodontosaurus's sounds/)).toBeInTheDocument();
    await fireEvent.click(await screen.findByRole('button', { name: 'Replace Social call' }));
    expect(await screen.findByRole('radio', { name: 'Only Anax' })).toBeChecked();
    await fireEvent.click(screen.getByRole('button', { name: 'Choose files…' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Add to mod' }));

    expect(rpc.callsTo('sounds.forSpecies').at(-1)?.params).toEqual({ species: ANAX });
    await waitFor(() => expect(rpc.callsTo('mods.replaceSound')[0]?.params).toMatchObject({ id: 'carch-voice', event: call.event, species: ANAX }));
    await waitFor(() => expect(onDone).toHaveBeenCalledWith({ event: call.event, species: ANAX, skin: null }));
  });

  it("opens on a species given to it; a folder of sounds can be for one of that species' own skins", async () => {
    const skin = { ...modDetail().skins[0], id: 'scarred', key: 'carch-voice/scarred', species: ANAX, name: 'Scarred' };
    const other = { ...modDetail().skins[1], key: 'carch-voice/red', species: 'Carcharodontosaurus', name: 'Red' };
    const { rpc, platform } = setup({ species: ANAX, modSkins: [skin, other], onReplaceMany: vi.fn(async () => true) });
    rpc.on('sounds.matchFolder', () => ({ groups: [{ name: 'AnaxX_VoxSocialCall', files: ['D:\\pack\\a.wav'], sounds: [call] }], unmatched: [] }));
    platform.folders.push('D:\\pack');

    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Sounds of' })).toHaveValue(ANAX));
    expect(await screen.findByRole('region', { name: 'Anax sounds' })).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Replace from folder…' }));

    const who = await screen.findByRole('combobox', { name: 'Who hears them' });
    expect([...who.querySelectorAll('option')].map((o) => o.textContent)).toEqual(['Every Anax', 'Only the Scarred skin']);
    expect(rpc.callsTo('sounds.matchFolder')[0]?.params).toMatchObject({ species: ANAX });
  });
});
