import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { Tab } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { messages, renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import ModEditor from './ModEditor.svelte';

function setup() {
  const rpc = new FakeRpc()
    .on('mods.get', () => modDetail())
    .on('mods.check', () => ({ errors: [], warnings: ['red-spot/blue male diffuse: small'], missingCutouts: [] }))
    .on('mods.thumbnail', () => ({ file: null }))
    .on('mods.colorPreview', () => ({ files: [] }));
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, memoryStore());
  const titles: string[] = [];
  const tab = new Tab('tab-mod', session.log, { retitle: (_id, t) => void titles.push(t), openTool: () => null });
  tab.key = 'red-spot';
  tab.active = true;
  renderWith(ModEditor, session, {}, tab);
  return { rpc, platform, session, tab, titles };
}

describe('ModEditor', () => {
  it('lists the mod, its skins and replacements, and names the tab after the mod', async () => {
    const { titles } = setup();
    expect(await screen.findByRole('button', { name: 'Blue-green stripes' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Red spot' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'T_carch_alt1_male_D' })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('button', { name: /^Check/ })).toHaveTextContent('1'));
    expect(titles.at(-1)).toBe('Carch pack');
  });

  it('editing the details saves them with the revision', async () => {
    const { rpc } = setup();
    rpc.on('mods.setDetails', (p) => modDetail({ name: p.name, revision: 'r2' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Mod details' }));
    const name = screen.getByRole('textbox', { name: 'Name' });
    await fireEvent.input(name, { target: { value: 'Carch pack 2' } });
    await fireEvent.change(name);
    await waitFor(() => expect(rpc.callsTo('mods.setDetails')[0]?.params).toMatchObject({ id: 'red-spot', revision: 'r1', name: 'Carch pack 2', version: '1.0.0' }));
  });

  it('removing a texture replacement asks first', async () => {
    const { rpc, platform } = setup();
    rpc.on('mods.removeReplacement', () => modDetail({ replace: [], revision: 'r2' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'T_carch_alt1_male_D' }));
    platform.confirmAnswer = false;
    await fireEvent.click(screen.getByRole('button', { name: 'Remove' }));
    expect(rpc.callsTo('mods.removeReplacement')).toHaveLength(0);
    platform.confirmAnswer = true;
    await fireEvent.click(screen.getByRole('button', { name: 'Remove' }));
    await waitFor(() => expect(rpc.callsTo('mods.removeReplacement')[0]?.params).toMatchObject({ texture: 'T_carch_alt1_male_D' }));
  });

  it('adds a Mod menu and an undo target to its tab', async () => {
    const { tab } = setup();
    await screen.findByRole('button', { name: 'Blue-green stripes' });
    expect(tab.menus.map((m) => m.label)).toEqual(['Mod']);
    expect(tab.undo?.canUndo()).toBe(false);
  });

  it('a failed load explains itself', async () => {
    const rpc = new FakeRpc().on('mods.get', () => {
      throw new Error("There is no mod 'red-spot' in this workspace.");
    });
    const session = new Session(rpc, new FakePlatform(), memoryStore());
    const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
    tab.key = 'red-spot';
    renderWith(ModEditor, session, {}, tab);
    expect(await screen.findByText(/could not be opened/)).toBeInTheDocument();
    expect(messages(session, tab).join('\n')).toContain('no mod');
  });
});
