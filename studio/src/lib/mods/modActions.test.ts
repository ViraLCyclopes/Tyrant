import { describe, expect, it } from 'vitest';
import { RpcError } from '$lib/rpc/client';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { messages, testTab, workspaceStatus } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import { exportMod, importModZip } from './modActions';

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
    expect(rpc.callsTo('mods.export')[0]?.params).toEqual({ id: 'red-spot', out: 'D:\\out\\red-spot-1.0.0.zip' });
    expect(messages(session, tab).join('\n')).toContain('red-spot-1.0.0.zip');
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
});
