import { fireEvent, screen, waitFor, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { Tab } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import { ModDoc } from './modDoc.svelte';
import SkinPage from './SkinPage.svelte';

async function setup() {
  const rpc = new FakeRpc()
    .on('mods.get', () => modDetail())
    .on('mods.check', () => ({ errors: [], warnings: ['red-spot/blue male diffuse: small'], missingCutouts: [] }))
    .on('mods.thumbnail', () => ({ file: null }))
    .on('mods.colorPreview', () => ({ files: [] }));
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, memoryStore());
  const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
  tab.key = 'red-spot';
  const doc = new ModDoc('red-spot', rpc, tab, () => {});
  await doc.load();
  await doc.runCheck();
  renderWith(SkinPage, session, { doc, skin: doc.detail!.skins[0] }, tab);
  return { rpc, platform, session, doc };
}

describe('SkinPage', () => {
  it('renames the skin when the name field changes', async () => {
    const { rpc, doc } = await setup();
    rpc.on('mods.renameSkin', (p) => modDetail({ revision: 'r2', skins: modDetail().skins.map((s) => (s.id === p.skin ? { ...s, name: p.name } : s)) }));
    const name = screen.getByRole('textbox', { name: 'Skin name' });
    await fireEvent.input(name, { target: { value: 'Ocean' } });
    await fireEvent.change(name);
    await waitFor(() => expect(rpc.callsTo('mods.renameSkin')[0]?.params).toMatchObject({ skin: 'blue', name: 'Ocean', revision: 'r1' }));
    expect(doc.detail?.skins[0].name).toBe('Ocean');
  });

  it("shows the base skin's slots for each sex; the male diffuse is the mod's own", async () => {
    await setup();
    const male = screen.getByRole('group', { name: 'Male files' });
    expect(within(male).getAllByRole('listitem').map((li) => li.dataset.slot)).toEqual(['diffuse', 'normal', 'pattern']);
    expect(within(male).getByRole('button', { name: 'Use base for male diffuse' })).toBeEnabled();
    expect(within(male).getByRole('button', { name: 'Use base for male normal' })).toBeDisabled();
  });

  it('Replace… copies the picked PNG into the slot', async () => {
    const { rpc, platform } = await setup();
    platform.files.push('D:\\art\\normal.png');
    rpc.on('mods.setSkinFile', () => modDetail({ revision: 'r2' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Replace male normal…' }));
    await waitFor(() =>
      expect(rpc.callsTo('mods.setSkinFile')[0]?.params).toMatchObject({ skin: 'blue', sex: 'male', slot: 'normal', png: 'D:\\art\\normal.png' }),
    );
  });

  it('Remove skin asks, can delete the files, and sends it', async () => {
    const { rpc } = await setup();
    rpc.on('mods.removeSkin', () => modDetail({ revision: 'r2', skins: [modDetail().skins[1]] }));
    await fireEvent.click(screen.getByRole('button', { name: 'Remove skin…' }));
    const dialog = screen.getByRole('dialog', { name: /Remove/ });
    await fireEvent.click(within(dialog).getByRole('checkbox', { name: /Also delete its files/ }));
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Remove skin' }));
    await waitFor(() => expect(rpc.callsTo('mods.removeSkin')[0]?.params).toMatchObject({ skin: 'blue', deleteFiles: true }));
  });

  it("shows this skin's Check results in place", async () => {
    await setup();
    expect(await screen.findByText('red-spot/blue male diffuse: small')).toBeInTheDocument();
  });

  it('removing a skin with its files cannot be undone, and the dialog says so', async () => {
    const { rpc, doc } = await setup();
    rpc.on('mods.renameSkin', () => modDetail({ revision: 'r2' }));
    rpc.on('mods.removeSkin', () => modDetail({ revision: 'r3', skins: [modDetail().skins[1]] }));
    await doc.edit('mods.renameSkin', { skin: 'red', name: 'Red' });
    expect(doc.canUndo).toBe(true);
    await fireEvent.click(screen.getByRole('button', { name: 'Remove skin…' }));
    const dialog = screen.getByRole('dialog', { name: /Remove/ });
    await fireEvent.click(within(dialog).getByRole('checkbox', { name: /Also delete its files/ }));
    expect(dialog).toHaveTextContent(/cannot be undone/);
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Remove skin' }));
    await waitFor(() => expect(rpc.callsTo('mods.removeSkin')).toHaveLength(1));
    expect(doc.canUndo).toBe(false);
  });
});
