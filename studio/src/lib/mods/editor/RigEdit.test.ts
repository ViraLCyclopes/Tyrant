import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import type { ModDetail } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import RigEdit from './RigEdit.svelte';

function setup(hasModel: boolean, bones = [{ bone: 'Jaw', move: [0, 0.1, 0], rotate: [0, 0, 0, 1], scale: [1, 1, 1] }]) {
  const rpc = new FakeRpc().on('mods.rig', () => ({ bones, hasModel, errors: [], warnings: ['Skin long: Jaw is moved by the game\'s animations: the rig edit changes that motion.'] }));
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, memoryStore());
  const doc = { id: 'long-anax', detail: { revision: 'r1' } as unknown as ModDetail, edit: vi.fn(async () => true) };
  return { rpc, session, doc, platform };
}

describe('RigEdit', () => {
  it('lists the edited bones with only what each changes, and the warnings', async () => {
    const { rpc, session, doc } = setup(true);
    renderWith(RigEdit, session, { doc, target: 'Allosaurus Anax', skin: 'long' });

    expect(await screen.findByText('Jaw')).toBeInTheDocument();
    expect(screen.getByText('move 0, 0.1, 0')).toBeInTheDocument();
    expect(screen.queryByText(/rotate/)).toBeNull();
    expect(screen.getByText(/moved by the game's animations/)).toBeInTheDocument();
    expect(rpc.callsTo('mods.rig')[0]?.params).toEqual({ id: 'long-anax', target: 'Allosaurus Anax', skin: 'long' });
  });

  it('clears the rig edit, asking first when a model was made for it', async () => {
    const { session, doc, platform } = setup(true);
    renderWith(RigEdit, session, { doc, target: 'Allosaurus Anax', skin: 'long' });

    await fireEvent.click(await screen.findByRole('button', { name: 'Clear rig edit' }));

    await waitFor(() => expect(doc.edit).toHaveBeenCalledWith('mods.clearRig', { target: 'Allosaurus Anax', skin: 'long' }));
    expect(platform.confirms[0]).toMatch(/made for the edited skeleton/);
  });

  it('shows nothing to clear without a rig edit, and says where rig edits are made', async () => {
    const { session, doc } = setup(false, []);
    renderWith(RigEdit, session, { doc, target: 'Allosaurus Anax', skin: null });

    expect(await screen.findByText(/No rig edit/)).toBeInTheDocument();
    expect(screen.getByText(/Open in Blender/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Clear rig edit' })).toBeNull();
  });
});
