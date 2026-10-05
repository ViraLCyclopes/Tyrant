import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import type { ModDetail } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import AddModel from './AddModel.svelte';

function setup(hasDump = true) {
  lastPlatform = new FakePlatform();
  const platform = lastPlatform;
  platform.files.push('D:\\blender\\big-carch.glb');
  const rpc = new FakeRpc().on('mods.species', () => ({
    hasDump,
    species: hasDump
      ? [
          { speciesId: 'Allosaurus Anax', vivarium: false, skins: [] },
          { speciesId: 'Carcharodontosaurus', vivarium: false, skins: [] },
        ]
      : [],
  }));
  const session = new Session(rpc, platform, memoryStore());
  const doc = { id: 'big-carch', detail: { revision: 'r1', models: [] } as unknown as ModDetail, edit: vi.fn(async () => true) };
  const onDone = vi.fn();
  renderWith(AddModel, session, { doc, onDone });
  return { doc, onDone };
}

let lastPlatform: FakePlatform;

describe('AddModel', () => {
  it('lets you pick a .glb or an .fbx', async () => {
    setup();

    await fireEvent.click(await screen.findByRole('button', { name: 'Browse…' }));

    await waitFor(() => expect(lastPlatform.fileDialogs[0]?.extensions).toEqual(['glb', 'fbx']));
    expect(lastPlatform.fileDialogs[0]?.title).toContain('.fbx');
  });

  it("adds the .glb as the chosen species' model in this mod and opens it", async () => {
    const { doc, onDone } = setup();

    const species = await screen.findByRole('combobox', { name: 'Species' });
    await fireEvent.change(species, { target: { value: 'Carcharodontosaurus' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Browse…' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Add' }));

    await waitFor(() => expect(doc.edit).toHaveBeenCalledWith('mods.replaceModel', { file: 'D:\\blender\\big-carch.glb', target: 'Carcharodontosaurus' }));
    await waitFor(() => expect(onDone).toHaveBeenCalledWith('Carcharodontosaurus'));
  });

  it('waits for a file before adding', async () => {
    setup();

    expect(await screen.findByRole('button', { name: 'Add' })).toBeDisabled();
  });

  it('asks for the data dump when there is none', async () => {
    setup(false);

    expect(await screen.findByText(/Run data dump/)).toBeInTheDocument();
  });
});
