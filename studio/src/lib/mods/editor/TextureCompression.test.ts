import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import type { ModTextureDto } from '$lib/rpc/types.gen';
import { Tab } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import { ModDoc } from './modDoc.svelte';
import TextureCompression from './TextureCompression.svelte';

const diffuse = (over: Partial<ModTextureDto> = {}): ModTextureDto => ({
  file: 'skins/blue/male_D.png', kind: 'colour', mode: 'auto', override: false, format: 'DXT1', width: 4096, height: 4096, bytes: 11_184_808, ...over,
});

async function setup(row: ModTextureDto) {
  const rpc = new FakeRpc().on('mods.get', () => modDetail()).on('mods.setCompression', () => modDetail({ revision: 'r2' }));
  const session = new Session(rpc, new FakePlatform(), memoryStore());
  const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
  const doc = new ModDoc('red-spot', rpc, tab, () => {});
  await doc.load();
  renderWith(TextureCompression, session, { doc, file: 'skins/blue/male_D.png', facts: { compression: 'auto', textures: [row], totalBytes: row.bytes } }, tab);
  return { rpc };
}

describe('TextureCompression', () => {
  it('shows the mod default chosen and what the texture becomes', async () => {
    await setup(diffuse());

    expect(screen.getByRole('combobox', { name: 'Compression of skins/blue/male_D.png' })).toHaveValue('');
    expect(screen.getByText('DXT1 · 11 MB in game')).toBeInTheDocument();
  });

  it('choosing BC7 sends one edit for that file', async () => {
    const { rpc } = await setup(diffuse());

    await fireEvent.change(screen.getByRole('combobox', { name: 'Compression of skins/blue/male_D.png' }), { target: { value: 'bc7' } });

    await waitFor(() => expect(rpc.callsTo('mods.setCompression')).toHaveLength(1));
    expect(rpc.callsTo('mods.setCompression')[0]?.params).toMatchObject({ file: 'skins/blue/male_D.png', mode: 'bc7', revision: 'r1' });
  });

  it('choosing Mod default clears the override', async () => {
    const { rpc } = await setup(diffuse({ mode: 'bc7', override: true, format: 'BC7' }));

    await fireEvent.change(screen.getByRole('combobox', { name: 'Compression of skins/blue/male_D.png' }), { target: { value: '' } });

    await waitFor(() => expect(rpc.callsTo('mods.setCompression')[0]?.params).toMatchObject({ file: 'skins/blue/male_D.png', mode: null }));
  });
});
