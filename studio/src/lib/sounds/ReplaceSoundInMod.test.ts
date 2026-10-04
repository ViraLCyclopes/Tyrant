import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import type { SoundDto } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { messages, renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import { Session } from '$lib/stores/session.svelte';
import ReplaceSoundInMod from './ReplaceSoundInMod.svelte';

const roar: SoundDto = {
  event: 'event:/AnimalFamily Master/Dinosaurs/Shared_TheropodLarge/_TheroLarge_Comp/Vox/TheroLarge_VoxSocialCall',
  name: 'Social call', group: 'Calls', species: ['Acrocanthosaurus', 'Carcharodontosaurus'], lengthMs: 2400, oneShot: true, perAnimal: true,
};

function setup() {
  const platform = new FakePlatform();
  platform.fileLists.push(['D:\\sounds\\roar1.wav', 'D:\\sounds\\roar2.ogg']);
  const rpc = new FakeRpc()
    .on('mods.list', () => ({ mods: [{ id: 'carch-voice', name: 'Carch voice', state: 'workspace' }] }))
    .on('mods.create', () => ({ mods: [{ id: 'carch-voice', name: 'Carch voice', state: 'workspace' }, { id: 'new-one', name: 'new-one', state: 'workspace' }] }))
    .on('mods.replaceSound', () => modDetail({ id: 'carch-voice' }));
  return { rpc, platform, session: new Session(rpc, platform, memoryStore()) };
}

async function openAndPick() {
  await fireEvent.click(screen.getByRole('button', { name: 'Replace Social call' }));
  await fireEvent.click(await screen.findByRole('button', { name: 'Choose files…' }));
  await screen.findByText('roar1.wav, roar2.ogg');
}

describe('ReplaceSoundInMod', () => {
  it('replaces the sound for this species only by default', async () => {
    const { rpc, session } = setup();
    renderWith(ReplaceSoundInMod, session, { sound: roar, speciesId: 'Carcharodontosaurus' });

    await openAndPick();
    expect(screen.getByRole('radio', { name: 'Only Carcharodontosaurus' })).toBeChecked();
    await fireEvent.click(screen.getByRole('button', { name: 'Add to mod' }));

    await waitFor(() =>
      expect(rpc.callsTo('mods.replaceSound')[0]?.params).toEqual({
        id: 'carch-voice', event: roar.event, files: ['D:\\sounds\\roar1.wav', 'D:\\sounds\\roar2.ogg'], species: 'Carcharodontosaurus', skin: null,
      }),
    );
    await waitFor(() => expect(messages(session, 'tab-test').join('\n')).toContain("Social call is now replaced in 'carch-voice' for Carcharodontosaurus"));
  });

  it('for every animal sends no species and says who else hears it', async () => {
    const { rpc, session } = setup();
    renderWith(ReplaceSoundInMod, session, { sound: roar, speciesId: 'Carcharodontosaurus' });

    await openAndPick();
    await fireEvent.click(screen.getByRole('radio', { name: 'For every animal that uses it' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Add to mod' }));

    await waitFor(() => expect((rpc.callsTo('mods.replaceSound')[0]?.params as { species: string | null }).species).toBeNull());
    await waitFor(() => expect(messages(session, 'tab-test').join('\n')).toContain('Acrocanthosaurus hears it too'));
  });

  it('from All sounds there is no species choice', async () => {
    const { rpc, session } = setup();
    renderWith(ReplaceSoundInMod, session, { sound: { ...roar, species: [] }, speciesId: null });

    await openAndPick();
    expect(screen.queryByRole('radio')).toBeNull();
    await fireEvent.click(screen.getByRole('button', { name: 'Add to mod' }));

    await waitFor(() => expect(rpc.callsTo('mods.replaceSound')[0]?.params).toMatchObject({ species: null, skin: null }));
  });

  it('a sound only menus play offers no species choice', async () => {
    const { rpc, session } = setup();
    renderWith(ReplaceSoundInMod, session, { sound: { ...roar, name: 'Nursery carch', perAnimal: false }, speciesId: 'Carcharodontosaurus' });

    await fireEvent.click(screen.getByRole('button', { name: 'Replace Nursery carch' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Choose files…' }));
    await screen.findByText('roar1.wav, roar2.ogg');

    expect(screen.queryByRole('radio')).toBeNull();
    expect(screen.getByText(/plays in menus/)).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Add to mod' }));
    await waitFor(() => expect(rpc.callsTo('mods.replaceSound')[0]?.params).toMatchObject({ species: null }));
  });

  it('can make a new mod for it', async () => {
    const { rpc, session } = setup();
    renderWith(ReplaceSoundInMod, session, { sound: roar, speciesId: 'Carcharodontosaurus' });

    await openAndPick();
    await fireEvent.change(screen.getByRole('combobox', { name: 'Mod' }), { target: { value: '__new__' } });
    await fireEvent.input(screen.getByRole('textbox', { name: 'New mod id' }), { target: { value: 'new-one' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Add to mod' }));

    await waitFor(() => expect(rpc.callsTo('mods.replaceSound')[0]?.params).toMatchObject({ id: 'new-one' }));
    expect(rpc.callsTo('mods.create')[0]?.params).toMatchObject({ id: 'new-one' });
  });

  it('asks for files before adding', async () => {
    const { rpc, session } = setup();
    renderWith(ReplaceSoundInMod, session, { sound: roar, speciesId: 'Carcharodontosaurus' });

    await fireEvent.click(screen.getByRole('button', { name: 'Replace Social call' }));

    expect(await screen.findByRole('button', { name: 'Add to mod' })).toBeDisabled();
    expect(rpc.callsTo('mods.replaceSound')).toHaveLength(0);
  });
});
