import { describe, expect, it } from 'vitest';
import { RpcError } from '$lib/rpc/client';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { messages, testTab, workspaceStatus } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import { Tab } from '$lib/shell/tab.svelte';
import { copyFromGame, deleteMod, exportMod, extractTextures, importModZip } from './modActions';

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
