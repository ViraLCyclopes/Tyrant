import { fireEvent, screen, waitFor, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import type { ModRow } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith, workspaceStatus, messages } from '$lib/test/fixtures';
import { Tab } from '$lib/shell/tab.svelte';
import { Session } from '$lib/stores/session.svelte';
import ModsView from './ModsView.svelte';

const row = (over: Partial<ModRow> = {}): ModRow => ({
  id: 'red-spot', name: 'Red spot', version: '1.0.0', author: null, replacements: 1, skins: 0, state: 'notInstalled', enabled: null,
  dir: 'D:\\ws\\mods\\red-spot', error: null, ...over,
});

function setup(mods: ModRow[], frameworkInstalled = false) {
  const rpc = new FakeRpc().on('mods.list', () => ({ mods, frameworkInstalled })).on('workspace.status', () => workspaceStatus());
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, memoryStore());
  session.workspace = workspaceStatus();
  return { rpc, platform, session };
}

describe('ModsView', () => {
  it('adds a mod from a zip', async () => {
    const { rpc, platform, session } = setup([]);
    rpc.on('mods.import', () => ({ id: 'shared-mod', problems: [], mods: { mods: [row({ id: 'shared-mod', name: 'Shared' })], frameworkInstalled: false, frameworkOutdated: false } }));
    platform.files.push('D:\\downloads\\shared-mod.zip');
    renderWith(ModsView, session);

    await fireEvent.click(await screen.findByRole('button', { name: 'Add mod from zip…' }));

    expect(await screen.findByText('Shared')).toBeInTheDocument();
    expect(rpc.callsTo('mods.import')[0]?.params).toEqual({ file: 'D:\\downloads\\shared-mod.zip', replace: false });
  });

  it('says when the framework in the game is out of date and updates it', async () => {
    const rpc = new FakeRpc()
      .on('mods.list', () => ({ mods: [], frameworkInstalled: true, frameworkOutdated: true }))
      .on('workspace.status', () => workspaceStatus())
      .on('dump.install', () => ({ installedLoader: false, message: 'Updated' }));
    const session = new Session(rpc, new FakePlatform(), memoryStore());
    session.workspace = workspaceStatus();
    renderWith(ModsView, session);

    expect(await screen.findByText(/framework in the game is older than this Tyrant/)).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Update Tyrant in game' }));

    await waitFor(() => expect(rpc.callsTo('dump.install')).toHaveLength(1));
  });

  it('lists mods with their state', async () => {
    const { session } = setup([row(), row({ id: 'blue', name: 'Blue', state: 'installed', enabled: true })]);
    renderWith(ModsView, session);

    expect(await screen.findByText('Red spot')).toBeInTheDocument();
    expect(screen.getByText('Not installed')).toBeInTheDocument();
    expect(screen.getByText('Installed')).toBeInTheDocument();
  });

  it('notes a species model or rig edit a later mod overrides', async () => {
    const clash = "Allosaurus Anax: its model or rig edit is overridden by 'short-anax' (later in the load order).";
    const { session } = setup([row({ id: 'long-anax', name: 'Long Anax', state: 'installed', enabled: true, clashes: [clash] })]);
    renderWith(ModsView, session);

    expect(await screen.findByText(clash)).toBeInTheDocument();
  });

  it('says which mods it uses species from and how many skins are skipped without them', async () => {
    const { session } = setup([row({
      id: 'mod-b', name: 'Mod B', usesSpeciesFrom: [
        { modId: 'mod-a', modName: 'Mod A', skins: 3, inWorkspace: true, installed: false, enabled: false },
        { modId: 'mod-c', modName: 'Mod C', skins: 1, inWorkspace: false, installed: true, enabled: false },
        { modId: 'mod-d', modName: 'Mod D', skins: 2, inWorkspace: false, installed: true, enabled: true },
      ],
    })]);
    renderWith(ModsView, session);

    expect(await screen.findByText(/Uses species from: Mod A/)).toBeInTheDocument();
    expect(screen.getByText('(not installed — 3 skins skipped)')).toBeInTheDocument();
    expect(screen.getByText('(off — 1 skin skipped)')).toBeInTheDocument();
    expect(screen.getByText('Uses species from: Mod D')).toBeInTheDocument(); // installed and on: nothing skipped
  });

  it('creates a mod', async () => {
    const { rpc, session } = setup([]);
    rpc.on('mods.create', () => ({ mods: [row({ id: 'blue-stripes', name: 'blue-stripes' })], frameworkInstalled: false, frameworkOutdated: false }));
    renderWith(ModsView, session);

    await fireEvent.input(await screen.findByRole('textbox', { name: 'Mod id' }), { target: { value: 'blue-stripes' } });
    await fireEvent.click(screen.getByRole('button', { name: 'New mod' }));

    await waitFor(() => expect(rpc.callsTo('mods.create')[0]?.params).toEqual({ id: 'blue-stripes', name: null, author: null }));
    expect(await screen.findByText('blue-stripes')).toBeInTheDocument();
  });

  it('installs after confirming, mentioning the framework when it is missing', async () => {
    const { rpc, platform, session } = setup([row()]);
    rpc.on('mods.install', () => ({ message: "Installed 'red-spot' into the game. Start the game to see it.", warnings: [] }));
    renderWith(ModsView, session);

    await fireEvent.click(await screen.findByRole('button', { name: 'Install Red spot to the game' }));

    await waitFor(() => expect(messages(session, 'tab-test').join('\n')).toContain("Installed 'red-spot'"));
    expect(platform.confirms[0]).toContain('framework');
    expect(rpc.callsTo('mods.install')[0]?.params).toEqual({ id: 'red-spot' });
  });

  it('says Update for a mod edited since it was installed and Refresh for one that is up to date', async () => {
    const { rpc, platform, session } = setup([row({ state: 'changed', enabled: true }), row({ id: 'blue', name: 'Blue', state: 'installed', enabled: true })], true);
    rpc.on('mods.install', () => ({ message: 'Installed.', warnings: [] }));
    renderWith(ModsView, session);

    expect(await screen.findByRole('button', { name: 'Update Red spot in the game' })).toHaveTextContent('Update in game');
    expect(screen.getByRole('button', { name: 'Refresh Blue in the game' })).toHaveTextContent('Refresh in game');
    await fireEvent.click(screen.getByRole('button', { name: 'Update Red spot in the game' }));

    await waitFor(() => expect(platform.confirms[0]).toContain('Copy your changes'));
    await fireEvent.click(screen.getByRole('button', { name: 'Refresh Blue in the game' }));
    await waitFor(() => expect(platform.confirms[1]).toContain('already in the game and up to date'));
  });

  it('does not install when the confirmation is declined', async () => {
    const { rpc, platform, session } = setup([row()]);
    platform.confirmAnswer = false;
    renderWith(ModsView, session);

    await fireEvent.click(await screen.findByRole('button', { name: 'Install Red spot to the game' }));

    expect(rpc.callsTo('mods.install')).toHaveLength(0);
  });

  it('shows check results under the mod', async () => {
    const { rpc, session } = setup([row()]);
    rpc.on('mods.check', () => ({ errors: ['T_x_D: textures/x.png is missing.'], warnings: ['T_y_D: wrong size.'] }));
    renderWith(ModsView, session);

    await fireEvent.click(await screen.findByRole('button', { name: 'Check Red spot' }));

    expect(await screen.findByText('T_x_D: textures/x.png is missing.')).toBeInTheDocument();
    expect(screen.getByText('T_y_D: wrong size.')).toBeInTheDocument();
  });

  it('turns an installed mod off', async () => {
    const { rpc, session } = setup([row({ state: 'installed', enabled: true })], true);
    rpc.on('mods.enable', () => ({ mods: [row({ state: 'installed', enabled: false })], frameworkInstalled: true, frameworkOutdated: false }));
    renderWith(ModsView, session);

    await fireEvent.click(await screen.findByRole('checkbox', { name: 'Red spot on' }));

    await waitFor(() => expect(rpc.callsTo('mods.enable')[0]?.params).toEqual({ id: 'red-spot', enabled: false }));
  });

  it('puts the On checkbox back when switching fails', async () => {
    const { rpc, session } = setup([row({ state: 'installed', enabled: true })], true);
    rpc.on('mods.enable', () => {
      throw new Error('Prehistoric Kingdom is running; close the game first.');
    });
    renderWith(ModsView, session);
    const box = (await screen.findByRole('checkbox', { name: 'Red spot on' })) as HTMLInputElement;

    await fireEvent.click(box);

    await waitFor(() => expect(box.checked).toBe(true));
  });

  it('says what each mod does', async () => {
    const { session } = setup([row({ replacements: 1, skins: 2 })]);
    renderWith(ModsView, session);

    expect(await screen.findByText('Replaces 1 texture · Adds 2 skins')).toBeInTheDocument();
  });

  it('offers to restore cutouts a check found missing, then checks again', async () => {
    const { rpc, session } = setup([row()]);
    let missing = ['skins/raptor/male_D.png'];
    rpc.on('mods.check', () => ({ errors: [], warnings: ['skins/raptor/male_D.png has no see-through pixels…'], missingCutouts: missing }));
    rpc.on('mods.restoreCutouts', () => {
      const restored = missing;
      missing = [];
      return { restored, problems: [] };
    });
    renderWith(ModsView, session);
    await fireEvent.click(await screen.findByRole('button', { name: 'Check Red spot' }));

    await fireEvent.click(await screen.findByRole('button', { name: 'Restore cutouts for Red spot' }));

    await waitFor(() => expect(messages(session, 'tab-test').join('\n')).toContain('1 PNG'));
    expect(rpc.callsTo('mods.restoreCutouts')[0]?.params).toEqual({ id: 'red-spot' });
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Restore cutouts for Red spot' })).toBeNull());
  });

  it('Open shows the mod in its own editor tab', async () => {
    const { session } = setup([row()]);
    const opened: unknown[] = [];
    const tab = new Tab('tab-test', session.log, { retitle: () => {}, openTool: (id, options) => (opened.push([id, options]), 'tab-2') });
    renderWith(ModsView, session, {}, tab);
    await fireEvent.click(await screen.findByRole('button', { name: 'Open Red spot' }));
    expect(opened).toEqual([['mod', { key: 'red-spot', title: 'Red spot' }]]);
  });

  it('a new mod opens in its editor tab', async () => {
    const { rpc, session } = setup([]);
    rpc.on('mods.create', (p) => ({ mods: [row({ id: p.id, name: p.name ?? p.id })], frameworkInstalled: false, frameworkOutdated: false }));
    const opened: unknown[] = [];
    const tab = new Tab('tab-test', session.log, { retitle: () => {}, openTool: (id, options) => (opened.push([id, options]), 'tab-2') });
    renderWith(ModsView, session, {}, tab);
    await fireEvent.input(await screen.findByRole('textbox', { name: /id/i }), { target: { value: 'blue-pack' } });
    await fireEvent.click(screen.getByRole('button', { name: 'New mod' }));
    await waitFor(() => expect(opened).toEqual([['mod', { key: 'blue-pack', title: 'blue-pack' }]]));
  });

  it('Delete asks, can keep the game copy, and refreshes the list', async () => {
    const { rpc, session } = setup([row({ state: 'installed', enabled: true })]);
    rpc.on('mods.delete', () => ({ mods: [], frameworkInstalled: false, frameworkOutdated: false }));
    renderWith(ModsView, session);

    await fireEvent.click(await screen.findByRole('button', { name: 'Delete Red spot' }));
    const dialog = screen.getByRole('dialog', { name: 'Delete Red spot' });
    expect(dialog).toHaveTextContent(/Recycle Bin/);
    await fireEvent.click(within(dialog).getByRole('checkbox', { name: 'Also remove it from the game' }));
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Delete mod' }));

    await waitFor(() => expect(rpc.callsTo('mods.delete')[0]?.params).toEqual({ id: 'red-spot', removeFromGame: false }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(screen.queryByText('Red spot')).toBeNull();
  });

  it('a delete confirmed twice in a row sends one mods.delete', async () => {
    const { rpc, session } = setup([row()]);
    let finish: (v: unknown) => void = () => {};
    rpc.on('mods.delete', () => new Promise((resolve) => (finish = resolve)));
    renderWith(ModsView, session);

    await fireEvent.click(await screen.findByRole('button', { name: 'Delete Red spot' }));
    const dialog = screen.getByRole('dialog', { name: 'Delete Red spot' });
    const delete1 = within(dialog).getByRole('button', { name: 'Delete mod' });
    await fireEvent.click(delete1);
    await fireEvent.click(delete1);
    await fireEvent.keyDown(window, { key: 'Escape' });

    expect(rpc.callsTo('mods.delete')).toHaveLength(1);
    expect(within(dialog).getByRole('button', { name: 'Deleting…' })).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toBeDisabled();
    expect(screen.getByRole('dialog', { name: 'Delete Red spot' })).toBeInTheDocument();
    finish({ mods: [], frameworkInstalled: false, frameworkOutdated: false });
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(rpc.callsTo('mods.delete')).toHaveLength(1);
  });

  it('a mod only in the game has no Delete, and a mod not installed has no game checkbox', async () => {
    const { session } = setup([row(), row({ id: 'other', name: 'Other', state: 'gameOnly' })]);
    renderWith(ModsView, session);

    expect(screen.queryByRole('button', { name: 'Delete Other' })).toBeNull();
    await fireEvent.click(await screen.findByRole('button', { name: 'Delete Red spot' }));
    expect(screen.queryByRole('checkbox', { name: 'Also remove it from the game' })).toBeNull();
  });

  it('marks an installed mod whose textures are not cooked', async () => {
    const { session } = setup([row({ state: 'installed', enabled: true, cooked: 'stale' }), row({ id: 'other', name: 'Other', state: 'installed', enabled: true, cooked: 'current' })], true);
    renderWith(ModsView, session);

    expect(await screen.findAllByText(/Reinstall to cook textures/)).toHaveLength(1);
  });
});
