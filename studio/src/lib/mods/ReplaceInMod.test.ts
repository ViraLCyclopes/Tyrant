import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import type { AssetRow } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import ReplaceInMod from './ReplaceInMod.svelte';

const asset: AssetRow = {
  ref: 'carch.bundle#7', bundle: 'carch.bundle', pathId: 7, type: 'Texture2D', name: 'T_carcharodontosaurus_alt1_male_D',
  containerPath: 'Assets/Art/T_carcharodontosaurus_alt1_male_D.png', guid: 'e3583acd2b3b5b14c875f42d110d97ce', script: null,
};

const existing = { id: 'red-spot', name: 'Red spot', version: '1.0.0', author: null, replacements: 0, skins: 0, state: 'notInstalled', enabled: null, dir: 'D:\\ws\\mods\\red-spot', error: null };

function setup(mods: (typeof existing)[]) {
  const list = { mods, frameworkInstalled: false };
  const rpc = new FakeRpc().on('mods.list', () => list).on('mods.create', () => list).on('mods.replace', () => list);
  const platform = new FakePlatform();
  return { rpc, platform, session: new Session(rpc, platform, memoryStore()) };
}

describe('ReplaceInMod', () => {
  it('adds the texture to an existing mod, using the exported PNG by default', async () => {
    const { rpc, session } = setup([existing]);
    renderWith(ReplaceInMod, session, { asset });

    await fireEvent.click(screen.getByRole('button', { name: 'Replace in a mod…' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Add to mod' }));

    await waitFor(() => expect(rpc.callsTo('mods.replace')[0]?.params).toEqual({ id: 'red-spot', texture: 'carch.bundle#7', png: null }));
    expect(session.notice).toContain('Install it from the Mods tab');
  });

  it('creates a new mod first when there is none', async () => {
    const { rpc, session } = setup([]);
    renderWith(ReplaceInMod, session, { asset });

    await fireEvent.click(screen.getByRole('button', { name: 'Replace in a mod…' }));
    await fireEvent.input(await screen.findByRole('textbox', { name: 'New mod id' }), { target: { value: 'blue-stripes' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Add to mod' }));

    await waitFor(() => expect(rpc.callsTo('mods.replace')[0]?.params).toEqual({ id: 'blue-stripes', texture: 'carch.bundle#7', png: null }));
    expect(rpc.callsTo('mods.create')[0]?.params).toEqual({ id: 'blue-stripes', name: null, author: null });
  });

  it('uses the PNG picked with Browse', async () => {
    const { rpc, platform, session } = setup([existing]);
    platform.files.push('D:\\edits\\spots.png');
    renderWith(ReplaceInMod, session, { asset });

    await fireEvent.click(screen.getByRole('button', { name: 'Replace in a mod…' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Browse…' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Add to mod' }));

    await waitFor(() => expect(rpc.callsTo('mods.replace')[0]?.params).toEqual({ id: 'red-spot', texture: 'carch.bundle#7', png: 'D:\\edits\\spots.png' }));
  });

  it('keeps a mod it created selectable when adding the texture fails', async () => {
    const created = { ...existing, id: 'blue-stripes', name: 'blue-stripes' };
    let mods: (typeof existing)[] = [];
    const rpc = new FakeRpc()
      .on('mods.list', () => ({ mods, frameworkInstalled: false }))
      .on('mods.create', () => {
        mods = [created];
        return { mods, frameworkInstalled: false };
      })
      .on('mods.replace', () => {
        throw new Error('No PNG was given and the texture has not been exported yet.');
      });
    const session = new Session(rpc, new FakePlatform(), memoryStore());
    renderWith(ReplaceInMod, session, { asset });

    await fireEvent.click(screen.getByRole('button', { name: 'Replace in a mod…' }));
    await fireEvent.input(await screen.findByRole('textbox', { name: 'New mod id' }), { target: { value: 'blue-stripes' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Add to mod' }));

    await waitFor(() => expect((screen.getByRole('combobox', { name: 'Mod' }) as HTMLSelectElement).value).toBe('blue-stripes'));
  });
});
