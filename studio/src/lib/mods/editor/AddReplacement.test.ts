import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import AddReplacement from './AddReplacement.svelte';

function setup() {
  const platform = new FakePlatform();
  platform.files.push('D:\\paint\\anax_D.png');
  const rpc = new FakeRpc()
    .on('mods.species', () => ({ hasDump: true, species: [{ speciesId: 'Allosaurus Anax', vivarium: false, skins: [] }] }))
    .on('mods.speciesTextures', () => ({
      speciesId: 'Allosaurus Anax',
      textures: [
        { texture: 'T_allosaurus_alt3_D', slot: 'adult colour', skins: ['Pyroclastic'], sharedWith: [] },
        { texture: 'FurMaskEmpty', slot: 'adult fur', skins: ['Pyroclastic'], sharedWith: ['Acrocanthosaurus', 'Allosaurus Fragilis'] },
      ],
    }))
    .on('assets.list', () => ({ rows: [{ ref: 'r#1', bundle: 'b', pathId: 1, type: 'Texture2D', name: 'Fence_Wood_D', containerPath: null, guid: 'g', script: null }], total: 1, page: 0, pageSize: 100 }))
    .on('mods.replace', () => ({ mods: [] }));
  const session = new Session(rpc, platform, memoryStore());
  const onDone = vi.fn();
  renderWith(AddReplacement, session, { modId: 'ultimasaurus-allo', onDone });
  return { rpc, onDone };
}

describe('AddReplacement', () => {
  it("lists a species' skin textures and replaces one with the picked PNG in this mod", async () => {
    const { rpc, onDone } = setup();

    await fireEvent.change(await screen.findByRole('combobox', { name: 'Textures of' }), { target: { value: 'Allosaurus Anax' } });
    expect(await screen.findByText('adult colour')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Replace T_allosaurus_alt3_D' }));

    await waitFor(() => expect(rpc.callsTo('mods.replace')[0]?.params).toEqual({ id: 'ultimasaurus-allo', texture: 'T_allosaurus_alt3_D', png: 'D:\\paint\\anax_D.png' }));
    await waitFor(() => expect(onDone).toHaveBeenCalledWith('T_allosaurus_alt3_D'));
  });

  it('says when a texture is shared with other species', async () => {
    setup();

    await fireEvent.change(await screen.findByRole('combobox', { name: 'Textures of' }), { target: { value: 'Allosaurus Anax' } });

    expect(await screen.findByText('also used by 2 other species')).toBeInTheDocument();
  });

  it('can search any game texture', async () => {
    const { rpc } = setup();

    await fireEvent.change(await screen.findByRole('combobox', { name: 'Textures of' }), { target: { value: '__any__' } });
    await fireEvent.input(screen.getByRole('searchbox', { name: 'Find a texture' }), { target: { value: 'fence' } });

    expect(await screen.findByRole('button', { name: 'Replace Fence_Wood_D' })).toBeInTheDocument();
    expect(rpc.callsTo('assets.list').at(-1)?.params).toMatchObject({ filter: 'fence', type: 'Texture2D' });
  });
});
