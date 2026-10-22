import { fireEvent, screen, waitFor, within } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import type { ModDetail, ModModelRigPreviewDto } from '$lib/rpc/types.gen';
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
  const rpc = new FakeRpc().on('mods.readModelRig', () => {
    if (rigFails) throw new Error('could not read the model');
    return rigPreview;
  }).on('mods.species', () => ({
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
  const view = renderWith(AddModel, session, { doc, onDone });
  return { doc, onDone, view, rpc };
}

let lastPlatform: FakePlatform;
let rigFails = false;
let rigPreview: ModModelRigPreviewDto = { source: 'gameSkeleton', bones: [], current: null, question: 'none', changes: false, errors: [], warnings: [], rigFile: null };

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

    await waitFor(() => expect(doc.edit).toHaveBeenCalledWith('mods.replaceModel', { file: 'D:\\blender\\big-carch.glb', target: 'Carcharodontosaurus', rig: 'auto' }));
    await waitFor(() => expect(onDone).toHaveBeenCalledWith('Carcharodontosaurus'));
  });

  it('Add sends the chosen rig choice', async () => {
    rigPreview = { ...rigPreview, source: 'bones', question: 'useOrKeep', changes: true, current: [], bones: [{ bone: 'Jaw', move: [0, 1, 0], rotate: [0, 0, 0, 1], scale: [1, 1, 1] }] };
    const { doc, onDone } = setup();
    await fireEvent.click(await screen.findByRole('button', { name: 'Browse…' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Add' }));
    await fireEvent.click(await screen.findByRole('radio', { name: 'Keep the current one (0 bones)' }));
    expect(doc.edit).not.toHaveBeenCalled();
    await fireEvent.click(screen.getAllByRole('button', { name: 'Add' }).at(-1)!);

    await waitFor(() => expect(doc.edit).toHaveBeenCalledWith('mods.replaceModel', { file: 'D:\\blender\\big-carch.glb', target: 'Allosaurus Anax', rig: 'keepCurrent' }));
    await waitFor(() => expect(onDone).toHaveBeenCalled());
    rigPreview = { ...rigPreview, question: 'none', bones: [], changes: false };
  });

  async function openQuestion() {
    rigPreview = { ...rigPreview, source: 'bones', question: 'useOrKeep', changes: true, current: [], bones: [{ bone: 'Jaw', move: [0, 1, 0], rotate: [0, 0, 0, 1], scale: [1, 1, 1] }] };
    const made = setup();
    await fireEvent.click(await screen.findByRole('button', { name: 'Browse…' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Add' }));
    await screen.findByRole('group', { name: 'Rig edit' });
    return made;
  }

  it('Cancel on the rig question adds nothing', async () => {
    const { doc, onDone } = await openQuestion();
    await fireEvent.click(within(screen.getByRole('group', { name: 'Rig edit' })).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('group', { name: 'Rig edit' })).toBeNull());
    expect(doc.edit).not.toHaveBeenCalled();
    expect(onDone).not.toHaveBeenCalled();
  });

  it('Escape on the rig question adds nothing', async () => {
    const { doc } = await openQuestion();
    await fireEvent.keyDown(window, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('group', { name: 'Rig edit' })).toBeNull());
    expect(doc.edit).not.toHaveBeenCalled();
  });

  it('leaving with the question open calls nothing, and the next card shows no question', async () => {
    const { doc, view } = await openQuestion();
    view.unmount();
    setup();
    await screen.findByRole('button', { name: 'Browse…' });
    expect(screen.queryByRole('group', { name: 'Rig edit' })).toBeNull();
    expect(doc.edit).not.toHaveBeenCalled();
  });

  it('a model that cannot be read adds nothing', async () => {
    rigFails = true;
    const { doc, onDone, rpc } = setup();
    await fireEvent.click(await screen.findByRole('button', { name: 'Browse…' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Add' }));
    await waitFor(() => expect(rpc.callsTo('mods.readModelRig')).toHaveLength(1));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Add' })).toBeEnabled());
    expect(doc.edit).not.toHaveBeenCalled();
    expect(onDone).not.toHaveBeenCalled();
    rigFails = false;
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
