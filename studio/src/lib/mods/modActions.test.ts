import { describe, expect, it, vi } from 'vitest';
import { RpcError } from '$lib/rpc/client';
import type { ModRow } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { messages, testTab, workspaceStatus } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import { Tab } from '$lib/shell/tab.svelte';
import { copyFromGame, deleteMod, exportMod, extractTextures, importModZip, installMod, removeFromGame } from './modActions';

function setup() {
  const rpc = new FakeRpc().on('workspace.status', () => workspaceStatus());
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, memoryStore());
  session.workspace = workspaceStatus();
  return { rpc, platform, session, tab: testTab(session) };
}

describe('mod sharing actions', () => {
  it('export asks where to save, runs the job and reports the path', async () => {
    const { rpc, platform, session, tab } = setup();
    rpc.on('mods.export', () => ({ path: 'D:\\out\\red-spot-1.0.0.zip', warnings: [] }));
    platform.saves.push('D:\\out\\red-spot-1.0.0.zip');

    const ok = await exportMod(session, tab, 'red-spot', 'Red spot', '1.0.0');

    expect(ok).toBe(true);
    expect(rpc.callsTo('mods.export')[0]?.params).toEqual({ id: 'red-spot', out: 'D:\\out\\red-spot-1.0.0.zip', includePngs: false });
    expect(messages(session, tab).join('\n')).toContain('red-spot-1.0.0.zip');
  });

  it('export passes the choice to include the PNGs', async () => {
    const { rpc, platform, session, tab } = setup();
    rpc.on('mods.export', () => ({ path: 'D:\\out\\x.zip', warnings: [] }));
    platform.saves.push('D:\\out\\x.zip');

    await exportMod(session, tab, 'red-spot', 'Red spot', '1.0.0', true);

    expect(rpc.callsTo('mods.export')[0]?.params).toMatchObject({ includePngs: true });
  });

  it('import says how many textures came from the cooked pack', async () => {
    const { rpc, platform, session, tab } = setup();
    rpc.on('mods.import', () => ({ id: 'shared-mod', problems: [], mods: { mods: [], frameworkInstalled: false, frameworkOutdated: false }, extracted: 3 }));
    platform.files.push('D:\\downloads\\shared-mod.zip');

    await importModZip(session, tab);

    expect(messages(session, tab).join('\n')).toContain(
      "Added 'shared-mod' to the workspace. 3 textures were extracted from its cooked pack; they are slightly lower quality than the author's originals.",
    );
  });

  it('copy from game asks to replace a mod that exists, then copies again with replace', async () => {
    const { rpc, platform, session, tab } = setup();
    let first = true;
    rpc.on('mods.copyFromGame', () => {
      if (first) {
        first = false;
        throw new RpcError("This workspace already has a mod 'other'. Import again and confirm to replace it (Mods tab), or add --replace.", 'MOD_EXISTS');
      }
      return { id: 'other', problems: [], mods: { mods: [], frameworkInstalled: true, frameworkOutdated: false }, extracted: 2 };
    });

    const list = await copyFromGame(session, tab, 'other');

    expect(platform.confirms[0]).toContain('Replace it with the one in the game?');
    expect(rpc.callsTo('mods.copyFromGame').map((c) => c.params)).toEqual([{ id: 'other', replace: false }, { id: 'other', replace: true }]);
    expect(list).not.toBeNull();
    expect(messages(session, tab).join('\n')).toContain("Copied 'other' into the workspace. 2 textures were extracted");
  });

  it('extract textures asks for a pack and a folder, runs the job and reports the count', async () => {
    const { rpc, platform, session, tab } = setup();
    rpc.on('textures.extract', (p) => ({ written: 2, skipped: [], out: p.out }));
    platform.files.push('C:\\m\\textures.tpack');
    platform.folders.push('C:\\out');

    expect(await extractTextures(session, tab)).toBe(true);

    expect(rpc.callsTo('textures.extract')[0]?.params).toEqual({ pack: 'C:\\m\\textures.tpack', out: 'C:\\out' });
    expect(messages(session, tab).join('\n')).toContain('Extracted 2 textures into C:\\out.');
  });

  it('extract textures does nothing when either dialog is cancelled', async () => {
    const { rpc, platform, session, tab } = setup();
    expect(await extractTextures(session, tab)).toBe(false);
    platform.files.push('C:\\m\\textures.tpack');
    expect(await extractTextures(session, tab)).toBe(false);
    expect(rpc.callsTo('textures.extract')).toHaveLength(0);
  });

  it('export does nothing when the save dialog is cancelled', async () => {
    const { rpc, session, tab } = setup();

    expect(await exportMod(session, tab, 'red-spot', 'Red spot', '1.0.0')).toBe(false);
    expect(rpc.callsTo('mods.export')).toHaveLength(0);
  });

  it('import asks to replace a mod that exists, then imports again with replace', async () => {
    const { rpc, platform, session, tab } = setup();
    let first = true;
    rpc.on('mods.import', (p) => {
      if (first) {
        first = false;
        throw new RpcError("This workspace already has a mod 'shared-mod'. Import again and confirm to replace it (Mods tab), or add --replace.", 'MOD_EXISTS');
      }
      return { id: 'shared-mod', problems: [], mods: { mods: [], frameworkInstalled: false, frameworkOutdated: false }, replace: p.replace };
    });
    platform.files.push('D:\\downloads\\shared-mod.zip');

    const list = await importModZip(session, tab);

    expect(platform.confirms[0]).toContain("already has a mod 'shared-mod'");
    expect(rpc.callsTo('mods.import').map((c) => c.params)).toEqual([
      { file: 'D:\\downloads\\shared-mod.zip', replace: false },
      { file: 'D:\\downloads\\shared-mod.zip', replace: true },
    ]);
    expect(list).not.toBeNull();
  });

  it('deleteMod deletes, closes the mod tab and returns the list; a failure is returned for the dialog', async () => {
    const { rpc, session } = setup();
    rpc.on('mods.delete', () => ({ mods: [], frameworkInstalled: false, frameworkOutdated: false }));
    rpc.on('mods.list', () => ({ mods: [], frameworkInstalled: false, frameworkOutdated: false })); // not in the game: no looks to ask about
    const closed: [string, string][] = [];
    const tab = new Tab('tab-test', session.log, { retitle: () => {}, openTool: () => null, closeTool: (tool, key) => void closed.push([tool, key]) });

    const ok = await deleteMod(session, tab, 'red-spot', 'Red spot', true);

    expect(ok).toEqual({ list: { mods: [], frameworkInstalled: false, frameworkOutdated: false } });
    expect(rpc.callsTo('mods.delete')[0]?.params).toEqual({ id: 'red-spot', removeFromGame: true });
    expect(closed).toEqual([['mod', 'red-spot']]);

    rpc.on('mods.delete', () => {
      throw new RpcError('Close the game first: removing a mod while it runs is refused.', 'GAME_RUNNING');
    });
    expect(await deleteMod(session, tab, 'red-spot', 'Red spot', true)).toEqual({ error: 'Close the game first: removing a mod while it runs is refused.' });
    expect(closed).toHaveLength(1); // a failed delete keeps the tab
  });

  it('deleteMod keeps its confirmation in the log when closing the tab clears the tab', async () => {
    const { rpc, session } = setup();
    rpc.on('mods.delete', () => ({ mods: [], frameworkInstalled: false, frameworkOutdated: false }));
    const host: ConstructorParameters<typeof Tab>[2] = {
      retitle: () => {},
      openTool: () => null,
      closeTool: () => {
        tab.detach();
        session.log.clear('tab-test'); // what ShellState.close does with the closed tab
      },
    };
    const tab = new Tab('tab-test', session.log, host);

    await deleteMod(session, tab, 'red-spot', 'Red spot', false);

    expect(messages(session, null).join(' ')).toContain('Recycle Bin');
  });
});

function modRow(overrides: Partial<ModRow> = {}): ModRow {
  return { id: 'red-spot', name: 'Red spot', version: '1.0.0', author: null, replacements: 0, skins: 1, state: 'changed', enabled: true, dir: 'D:\\ws\\mods\\red-spot', error: null, gameLooks: true, ...overrides };
}

describe('looks made in the game', () => {
  const blue = { folder: 'materials/skins/blue', gameChanged: true, projectChanged: false };
  const both = { folder: 'materials/species/Carcharodontosaurus', gameChanged: true, projectChanged: true };
  const projectOnly = { folder: 'materials/vanilla-skins/Carcharodontosaurus-Alt 1', gameChanged: false, projectChanged: true };
  const empty = { mods: [], frameworkInstalled: true, frameworkOutdated: false };

  it('install asks to bring in game looks first', async () => {
    const { rpc, platform, session, tab } = setup();
    const order: string[] = [];
    rpc.on('mods.list', () => ({ mods: [modRow()], frameworkInstalled: true, frameworkOutdated: false }));
    rpc.on('mods.looksChanges', () => ({ changes: [blue, both, projectOnly] }));
    rpc.on('mods.bringInLooks', () => void order.push('bringInLooks'));
    rpc.on('mods.install', () => {
      order.push('install');
      return { message: 'Installed.', warnings: [] };
    });

    const done = installMod(session, tab, 'red-spot', 'Red spot');
    await vi.waitFor(() => expect(session.looksPrompt).toBeTruthy());

    expect(session.looksPrompt!.mods).toEqual([{ id: 'red-spot', name: 'Red spot', changes: [blue, both], gameOnly: false }]); // only what the game changed
    expect(platform.confirms).toHaveLength(0);
    session.looksPrompt!.answer({ bringIn: { 'red-spot': ['materials/skins/blue'] }, copy: [] });
    expect(await done).toBe(true);
    expect(rpc.callsTo('mods.looksChanges')[0]?.params).toEqual({ id: 'red-spot' });
    expect(rpc.callsTo('mods.bringInLooks')[0]?.params).toEqual({ id: 'red-spot', folders: ['materials/skins/blue'] });
    expect(order).toEqual(['bringInLooks', 'install']);
    expect(session.looksPrompt).toBeNull();
  });

  it('install stops when the looks dialog is cancelled', async () => {
    const { rpc, session, tab } = setup();
    rpc.on('mods.list', () => ({ mods: [modRow()], frameworkInstalled: true, frameworkOutdated: false }));
    rpc.on('mods.looksChanges', () => ({ changes: [blue] }));

    const done = installMod(session, tab, 'red-spot', 'Red spot');
    await vi.waitFor(() => expect(session.looksPrompt).toBeTruthy());
    session.looksPrompt!.answer(null);

    expect(await done).toBe(false);
    expect(rpc.callsTo('mods.bringInLooks')).toHaveLength(0);
    expect(rpc.callsTo('mods.install')).toHaveLength(0);
  });

  it('install does not ask when the game changed no looks', async () => {
    const { rpc, platform, session, tab } = setup();
    rpc.on('mods.list', () => ({ mods: [modRow({ gameLooks: false })], frameworkInstalled: true, frameworkOutdated: false }));
    rpc.on('mods.install', () => ({ message: 'Installed.', warnings: [] }));

    expect(await installMod(session, tab, 'red-spot', 'Red spot')).toBe(true);
    expect(rpc.callsTo('mods.looksChanges')).toHaveLength(0);
    expect(platform.confirms).toHaveLength(1);
  });

  it('removing a mod only in the game offers to copy it to the workspace first', async () => {
    const { rpc, session, tab } = setup();
    const order: string[] = [];
    rpc.on('mods.list', () => ({ mods: [modRow({ id: 'vanilla-looks', name: 'Vanilla Looks', state: 'gameOnly' })], frameworkInstalled: true, frameworkOutdated: false }));
    rpc.on('mods.copyFromGame', () => {
      order.push('copy');
      return { id: 'vanilla-looks', problems: [], mods: empty, extracted: 0 };
    });
    rpc.on('mods.remove', () => {
      order.push('remove');
      return empty;
    });

    const done = removeFromGame(session, tab, 'vanilla-looks', 'Vanilla Looks');
    await vi.waitFor(() => expect(session.looksPrompt).toBeTruthy());
    expect(session.looksPrompt!.mods).toEqual([{ id: 'vanilla-looks', name: 'Vanilla Looks', changes: [], gameOnly: true }]);
    session.looksPrompt!.answer({ bringIn: {}, copy: ['vanilla-looks'] });

    expect(await done).toBe(true);
    expect(order).toEqual(['copy', 'remove']);
  });

  it('deleting with remove from game brings the looks in first; cancelling deletes nothing', async () => {
    const { rpc, session, tab } = setup();
    rpc.on('mods.list', () => ({ mods: [modRow()], frameworkInstalled: true, frameworkOutdated: false }));
    rpc.on('mods.looksChanges', () => ({ changes: [blue] }));
    rpc.on('mods.delete', () => empty);

    const cancelled = deleteMod(session, tab, 'red-spot', 'Red spot', true);
    await vi.waitFor(() => expect(session.looksPrompt).toBeTruthy());
    session.looksPrompt!.answer(null);
    expect(await cancelled).toHaveProperty('error');
    expect(rpc.callsTo('mods.delete')).toHaveLength(0);

    expect(await deleteMod(session, tab, 'red-spot', 'Red spot', false)).toHaveProperty('list'); // kept in the game: nothing to ask
    expect(rpc.callsTo('mods.looksChanges')).toHaveLength(1);

    const order: string[] = [];
    rpc.on('mods.bringInLooks', () => void order.push('bringInLooks'));
    rpc.on('mods.delete', () => {
      order.push('delete');
      return empty;
    });
    const kept = deleteMod(session, tab, 'red-spot', 'Red spot', true);
    await vi.waitFor(() => expect(session.looksPrompt).toBeTruthy());
    session.looksPrompt!.answer({ bringIn: { 'red-spot': ['materials/skins/blue'] }, copy: [] });
    expect(await kept).toHaveProperty('list');
    expect(rpc.callsTo('mods.bringInLooks')[0]?.params).toEqual({ id: 'red-spot', folders: ['materials/skins/blue'] });
    expect(order).toEqual(['bringInLooks', 'delete']); // into the folder that goes to the Recycle Bin
  });

  it('removing a workspace mod brings its game looks in first', async () => {
    const { rpc, platform, session, tab } = setup();
    const order: string[] = [];
    rpc.on('mods.list', () => ({ mods: [modRow({ state: 'installed' })], frameworkInstalled: true, frameworkOutdated: false }));
    rpc.on('mods.looksChanges', () => ({ changes: [blue, both] }));
    rpc.on('mods.bringInLooks', () => void order.push('bringInLooks'));
    rpc.on('mods.remove', () => {
      order.push('remove');
      return empty;
    });

    const done = removeFromGame(session, tab, 'red-spot', 'Red spot');
    await vi.waitFor(() => expect(session.looksPrompt).toBeTruthy());
    expect(session.looksPrompt!.action).toBe("Removing 'Red spot' from the game");
    expect(platform.confirms).toHaveLength(0);
    session.looksPrompt!.answer({ bringIn: { 'red-spot': ['materials/skins/blue', 'materials/species/Carcharodontosaurus'] }, copy: [] });

    expect(await done).toBe(true);
    expect(rpc.callsTo('mods.bringInLooks')[0]?.params).toEqual({ id: 'red-spot', folders: ['materials/skins/blue', 'materials/species/Carcharodontosaurus'] });
    expect(order).toEqual(['bringInLooks', 'remove']);
  });

  it('when the list cannot be read, install still asks the core about looks instead of assuming none', async () => {
    const { rpc, session, tab } = setup();
    rpc.on('mods.list', () => {
      throw new RpcError('The workspace could not be read.', 'WORKSPACE_INVALID');
    });
    rpc.on('mods.looksChanges', () => ({ changes: [blue] }));

    const done = installMod(session, tab, 'red-spot', 'Red spot');
    await vi.waitFor(() => expect(session.looksPrompt).toBeTruthy());
    session.looksPrompt!.answer(null);

    expect(await done).toBe(false);
    expect(rpc.callsTo('mods.looksChanges')).toHaveLength(1);
    expect(rpc.callsTo('mods.install')).toHaveLength(0);
  });

  it('uninstall says so when it could not check the installed mods for looks, and stops when declined', async () => {
    const { rpc, platform, session, tab } = setup();
    rpc.on('mods.list', () => {
      throw new RpcError('The workspace could not be read.', 'WORKSPACE_INVALID');
    });
    rpc.on('dump.uninstall', () => ({ removedLoader: true, message: 'Removed.' }));
    platform.confirmAnswer = false;

    await session.uninstallDumper(tab);

    expect(platform.confirms[0]).toMatch(/^Tyrant could not list the installed mods \(The workspace could not be read\.\), so it could not check them for looks made in the game/);
    expect(session.looksPrompt).toBeNull();
    expect(rpc.callsTo('dump.uninstall')).toHaveLength(0);

    platform.confirmAnswer = true; // the user read it and goes ahead
    await session.uninstallDumper(tab);
    expect(rpc.callsTo('dump.uninstall')).toHaveLength(1);
  });

  it('uninstall brings the looks in first and says the animal looks are kept', async () => {
    const { rpc, platform, session, tab } = setup();
    rpc.on('mods.list', () => ({ mods: [modRow(), modRow({ id: 'vanilla-looks', name: 'Vanilla Looks', state: 'gameOnly' })], frameworkInstalled: true, frameworkOutdated: false }));
    rpc.on('mods.looksChanges', () => ({ changes: [blue] }));
    rpc.on('mods.bringInLooks', () => ({}));
    rpc.on('dump.uninstall', () => ({ removedLoader: true, message: 'Removed.' }));

    const done = session.uninstallDumper(tab);
    await vi.waitFor(() => expect(session.looksPrompt).toBeTruthy());
    expect(session.looksPrompt!.mods.map((m) => m.id)).toEqual(['red-spot', 'vanilla-looks']);
    session.looksPrompt!.answer({ bringIn: { 'red-spot': ['materials/skins/blue'] }, copy: [] });
    await done;

    expect(rpc.callsTo('mods.bringInLooks')).toHaveLength(1);
    expect(platform.confirms[0]).toContain('UserData\\Tyrant\\Looks');
    expect(platform.confirms[0]).toContain('UserData\\Tyrant\\Masks'); // looks point at images there
    expect(rpc.callsTo('dump.uninstall')).toHaveLength(1);
  });
});
