import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import type { AssetRow } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { messages, renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import { Session } from '$lib/stores/session.svelte';
import ReplaceModelInMod from './ReplaceModelInMod.svelte';

const prefab: AssetRow = { ref: 'carch#1', bundle: 'carch.bundle', pathId: 1, type: 'GameObject', name: 'Carcharodontosaurus', containerPath: 'Assets/Carch.prefab', guid: 'g', script: null };

const added = modDetail({
  models: [{ target: 'Carcharodontosaurus', skin: null, file: 'models/x.glb', lods: [{ file: 'a', vertices: 9000, index32: false, vanilla: 20000 }, { file: 'b', vertices: 3000, index32: false, vanilla: 7000 }], errors: [], warnings: ['Shape key \'Smile\' was left out.'], stale: false }],
});

function setup() {
  const platform = new FakePlatform();
  platform.files.push('D:\\blender\\carch.glb');
  const rpc = new FakeRpc()
    .on('mods.list', () => ({ mods: [{ id: 'big-carch', name: 'Big Carch', state: 'workspace' }] }))
    .on('mods.replaceModel', () => added);
  return { rpc, session: new Session(rpc, platform, memoryStore()) };
}

describe('ReplaceModelInMod', () => {
  it('sends the picked .glb to the chosen mod for this prefab', async () => {
    const { rpc, session } = setup();
    renderWith(ReplaceModelInMod, session, { asset: prefab });

    await fireEvent.click(screen.getByRole('button', { name: 'Replace model in a mod…' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Browse…' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Add to mod' }));

    await waitFor(() => expect(rpc.callsTo('mods.replaceModel')[0]?.params).toEqual({ id: 'big-carch', file: 'D:\\blender\\carch.glb', prefabRef: 'carch#1' }));
    await waitFor(() => expect(messages(session, 'tab-test').join('\n')).toContain("Carcharodontosaurus's model is now in 'big-carch' (2 LOD(s))"));
    expect(messages(session, 'tab-test').join('\n')).toContain("'Smile' was left out");
  });

  it('asks for a file before adding', async () => {
    const { rpc, session } = setup();
    renderWith(ReplaceModelInMod, session, { asset: prefab });

    await fireEvent.click(screen.getByRole('button', { name: 'Replace model in a mod…' }));

    expect(await screen.findByRole('button', { name: 'Add to mod' })).toBeDisabled();
    expect(rpc.callsTo('mods.replaceModel')).toHaveLength(0);
  });
});
