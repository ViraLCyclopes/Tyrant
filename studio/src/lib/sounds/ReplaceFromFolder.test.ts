import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import ReplaceFromFolder from './ReplaceFromFolder.svelte';

const sound = (event: string, name: string) => ({ event, name, group: 'Calls', species: ['Allosaurus Anax'], lengthMs: null, oneShot: true, perAnimal: true });
const angry = sound('event:/X/Vox/TheroMed_VoxAngry', 'Angry');
const yawnMed = sound('event:/X/Vox/TheroMed_VoxYawn', 'Yawn');
const yawnLarge = sound('event:/Y/Vox/TheroLarge_VoxYawn', 'Yawn');

function setup() {
  const platform = new FakePlatform();
  platform.folders.push('C:\\pack');
  const rpc = new FakeRpc().on('sounds.matchFolder', () => ({
    groups: [
      { name: 'AlloAnax_VoxAngry', files: ['C:\\pack\\AlloAnax_VoxAngry_01.wav', 'C:\\pack\\AlloAnax_VoxAngry_02.wav'], sounds: [angry] },
      { name: 'AlloAnax_VoxYawn', files: ['C:\\pack\\AlloAnax_VoxYawn_01.wav'], sounds: [yawnMed, yawnLarge] },
    ],
    unmatched: ['C:\\pack\\AlloAnax_Sneeze_01.wav'],
  }));
  const session = new Session(rpc, platform, memoryStore());
  const onReplace = vi.fn(async () => true);
  const skins = [{ key: 'Allosaurus Anax/Ultimasaurus', label: 'Ultimasaurus' }];
  renderWith(ReplaceFromFolder, session, { species: 'Allosaurus Anax', skins, onReplace });
  return { rpc, onReplace };
}

describe('ReplaceFromFolder', () => {
  it('matches a picked folder and replaces the ticked sounds with all their takes', async () => {
    const { rpc, onReplace } = setup();

    await fireEvent.click(screen.getByRole('button', { name: 'Replace from folder…' }));

    await waitFor(() => expect(rpc.callsTo('sounds.matchFolder')[0]?.params).toEqual({ folder: 'C:\\pack', species: 'Allosaurus Anax' }));
    expect(await screen.findByText('AlloAnax_VoxAngry')).toBeInTheDocument();
    expect(screen.getByText(/AlloAnax_Sneeze_01\.wav/)).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Replace 1 sound' }));

    expect(onReplace).toHaveBeenCalledWith([{ event: angry.event, files: ['C:\\pack\\AlloAnax_VoxAngry_01.wav', 'C:\\pack\\AlloAnax_VoxAngry_02.wav'] }], null);
  });

  it('the sounds can be for one skin only', async () => {
    const { onReplace } = setup();
    await fireEvent.click(screen.getByRole('button', { name: 'Replace from folder…' }));

    await fireEvent.change(await screen.findByRole('combobox', { name: 'Who hears them' }), { target: { value: 'Allosaurus Anax/Ultimasaurus' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Replace 1 sound' }));

    expect(onReplace).toHaveBeenCalledWith([expect.objectContaining({ event: angry.event })], 'Allosaurus Anax/Ultimasaurus');
  });

  it('a name that fits several sounds is replaced once one is picked', async () => {
    const { onReplace } = setup();
    await fireEvent.click(screen.getByRole('button', { name: 'Replace from folder…' }));

    await fireEvent.change(await screen.findByRole('combobox', { name: 'Sound for AlloAnax_VoxYawn' }), { target: { value: yawnLarge.event } });
    await fireEvent.click(screen.getByRole('button', { name: 'Replace 2 sounds' }));

    expect(onReplace).toHaveBeenCalledWith([
      { event: angry.event, files: ['C:\\pack\\AlloAnax_VoxAngry_01.wav', 'C:\\pack\\AlloAnax_VoxAngry_02.wav'] },
      { event: yawnLarge.event, files: ['C:\\pack\\AlloAnax_VoxYawn_01.wav'] },
    ], null);
  });

  it('unticking a row leaves that sound as the game has it', async () => {
    const { onReplace } = setup();
    await fireEvent.click(screen.getByRole('button', { name: 'Replace from folder…' }));

    await fireEvent.click(await screen.findByRole('checkbox', { name: 'Replace with AlloAnax_VoxAngry' }));

    expect(screen.getByRole('button', { name: 'Replace 0 sounds' })).toBeDisabled();
    expect(onReplace).not.toHaveBeenCalled();
  });
});
