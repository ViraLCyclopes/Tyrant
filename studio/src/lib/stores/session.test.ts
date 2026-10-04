import { waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { RpcError } from '$lib/rpc/client';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { installInfo, messages, testTab, workspaceStatus } from '$lib/test/fixtures';
import { Session, START_GAME_WARNING } from './session.svelte';

const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

function setup(stored: Record<string, string> = {}) {
  const rpc = new FakeRpc();
  const platform = new FakePlatform();
  const store = memoryStore(stored);
  return { rpc, platform, store, session: new Session(rpc, platform, store) };
}

describe('Session', () => {
  it('offers the framework update once per bundled framework version, only when it is out of date', async () => {
    const { platform, session } = setup();
    platform.confirmAnswer = false;
    session.workspace = workspaceStatus({ framework: 'outdated', frameworkInGame: '0.3.0', frameworkBundled: '0.4.0' });

    await session.offerFrameworkUpdate();
    await session.offerFrameworkUpdate();
    session.workspace = workspaceStatus({ framework: 'missing', frameworkBundled: '0.5.0' });
    await session.offerFrameworkUpdate();

    expect(platform.confirms).toHaveLength(1); // once, and never for a framework that was never installed
    expect(platform.confirms[0]).toContain('0.4.0');
  });

  it('accepting the framework offer runs the update job', async () => {
    const { rpc, session } = setup();
    rpc.on('dump.install', () => ({ installedLoader: false, message: 'Updated' })).on('workspace.status', () => workspaceStatus());
    session.workspace = workspaceStatus({ framework: 'outdated', frameworkInGame: '0.3.0', frameworkBundled: '0.4.0' });

    await session.offerFrameworkUpdate();

    expect(rpc.callsTo('dump.install')).toHaveLength(1);
  });

  it('reopens the last workspace on start', async () => {
    const { rpc, session } = setup({ 'tyrant.lastWorkspace': 'D:\\ws' });
    rpc.on('workspace.open', () => workspaceStatus());

    await session.start();

    expect(session.workspace?.dir).toBe('D:\\ws');
    expect(session.install?.rootDir).toBe('G:\\PK');
    expect(session.ready).toBe(true);
  });

  it('forgets a last workspace that no longer opens and looks for the game', async () => {
    const { rpc, store, session } = setup({ 'tyrant.lastWorkspace': 'D:\\gone' });
    rpc.on('workspace.open', () => {
      throw new RpcError("'D:\\gone' is not a workspace.", 'WORKSPACE_INVALID', 'PICK_WORKSPACE_FOLDER');
    });
    rpc.on('install.detect', () => installInfo());

    await session.start();

    expect(session.workspace).toBeNull();
    expect(session.install?.buildGuid).toBe('b1');
    expect(session.error?.code).toBe('WORKSPACE_INVALID');
    expect(store.get('tyrant.lastWorkspace')).toBeNull();
  });

  it('remembers up to five recent workspaces, newest first', async () => {
    const { rpc, store, session } = setup();
    rpc.on('workspace.open', (p) => workspaceStatus({ dir: p.dir }));

    for (const dir of ['a', 'b', 'c', 'd', 'e', 'f', 'B']) await session.openWorkspace(dir);

    expect(session.recent).toEqual(['B', 'f', 'e', 'd', 'c']);
    expect(JSON.parse(store.get('tyrant.recentWorkspaces')!)).toEqual(['B', 'f', 'e', 'd', 'c']);
  });

  it('drops a recent workspace whose folder no longer opens', async () => {
    const { rpc, store, session } = setup({ 'tyrant.recentWorkspaces': JSON.stringify(['D:\\gone', 'D:\\ok']) });
    rpc.on('workspace.open', () => {
      throw new RpcError("'D:\\gone' is not a workspace.", 'WORKSPACE_INVALID', 'PICK_WORKSPACE_FOLDER');
    });

    await session.openWorkspace('D:\\gone');

    expect(session.recent).toEqual(['D:\\ok']);
    expect(JSON.parse(store.get('tyrant.recentWorkspaces')!)).toEqual(['D:\\ok']);
  });

  it('runJob shows progress, returns the result and refreshes the status', async () => {
    const { rpc, session } = setup();
    const seen: number[] = [];
    session.workspace = workspaceStatus();
    rpc.on('decompile.run', (_p, onProgress) => {
      onProgress?.(0.5, 'Decompiling Assembly-CSharp');
      seen.push(session.job!.fraction);
      return { assemblies: [] };
    });
    rpc.on('workspace.status', () => workspaceStatus({ hasSource: true }));

    const result = await session.runJob('decompile.run', {}, 'Decompile');

    expect(result).toEqual({ assemblies: [] });
    expect(seen).toEqual([0.5]);
    expect(session.job).toBeNull();
    expect(session.workspace?.hasSource).toBe(true);
  });

  it('keeps the error of a failed job after refreshing the status', async () => {
    const { rpc, session } = setup();
    session.workspace = workspaceStatus({ dumper: 'installed' });
    rpc.on('dump.run', () => {
      throw new RpcError('No dump arrived.', 'DUMP_TIMEOUT');
    });
    rpc.on('workspace.status', () => workspaceStatus({ dumper: 'installed' }));

    expect(await session.runDump()).toBeNull();
    expect(session.error?.code).toBe('DUMP_TIMEOUT');
  });

  it('a cancelled job without a tab is a core-wide record, not an error', async () => {
    const { rpc, session } = setup();
    rpc.on('decompile.run', () => {
      throw new RpcError('Cancelled.', 'CANCELLED');
    });

    await session.runJob('decompile.run', {}, 'Decompile');

    expect(session.error).toBeNull();
    expect(messages(session, null)).toContain('Decompile was cancelled.');
  });

  it('refuses a second job while one runs', async () => {
    const { rpc, session } = setup();
    let release!: () => void;
    rpc.on('assets.index', () => new Promise((resolve) => (release = () => resolve({ assets: 1, failures: 0, missingBundles: 0 }))));
    rpc.on('decompile.run', () => ({ assemblies: [] }));

    const first = session.runJob('assets.index', undefined, 'Asset index');
    await flush();
    expect(session.busy).toBe(true);
    expect(await session.runJob('decompile.run', {}, 'Decompile')).toBeNull();
    release();
    await first;

    expect(rpc.callsTo('decompile.run')).toHaveLength(0);
    expect(session.busy).toBe(false);
  });

  it('asks before starting the game for a dump; declining does nothing', async () => {
    const { rpc, platform, session } = setup();
    platform.confirmAnswer = false;

    expect(await session.runDump()).toBeNull();

    expect(platform.confirms).toEqual([START_GAME_WARNING]);
    expect(rpc.callsTo('dump.run')).toHaveLength(0);
  });

  it('core exit stops the job and explains', () => {
    const { rpc, session } = setup();
    session.job = { id: 'j1', title: 'Data dump', fraction: 0.3, message: 'Waiting', cancel: null };

    rpc.emitExit({ code: 1, restarting: true, error: null });

    expect(session.job).toBeNull();
    expect(session.error?.code).toBe('SIDECAR_EXITED');
    expect(session.error?.message).toContain('restarted');
  });

  it('reopens the workspace after the core restarts', async () => {
    const { rpc, session } = setup();
    rpc.on('workspace.open', (p) => workspaceStatus({ dir: p.dir }));
    await session.openWorkspace('D:\\ws');

    rpc.emitStarted();
    await flush();

    expect(rpc.callsTo('workspace.open')).toHaveLength(2);
    expect(rpc.callsTo('workspace.open')[1].params).toEqual({ dir: 'D:\\ws' });
  });

  it('PICK_GAME_FOLDER fix re-points the workspace that failed to open', async () => {
    const { rpc, platform, session } = setup({ 'tyrant.lastWorkspace': 'D:\\ws' });
    rpc.on('workspace.open', (p) => {
      if (!p.gamePath) throw new RpcError('The game folder moved.', 'GAME_NOT_FOUND', 'PICK_GAME_FOLDER');
      return workspaceStatus({ gameRoot: p.gamePath });
    });
    rpc.on('install.detect', () => {
      throw new RpcError('Not found.', 'GAME_NOT_FOUND', 'PICK_GAME_FOLDER');
    });
    await session.start();
    platform.folders.push('G:\\Moved');

    await session.applyFix('PICK_GAME_FOLDER');

    expect(rpc.callsTo('workspace.open').at(-1)?.params).toEqual({ dir: 'D:\\ws', gamePath: 'G:\\Moved' });
    expect(session.workspace?.gameRoot).toBe('G:\\Moved');
    expect(session.error).toBeNull();
  });

  it('copies diagnostics to the clipboard', async () => {
    const { rpc, platform, session } = setup();
    rpc.on('app.diagnostics', () => ({ text: 'Tyrant diagnostics' }));

    await session.copyDiagnostics();

    expect(platform.copied).toEqual(['Tyrant diagnostics']);
    expect(messages(session, null).at(-1)).toMatch(/copied/);
  });

  it('PICK_GAME_FOLDER fixes the workspace that failed to open, not the one already open', async () => {
    const { rpc, platform, session } = setup();
    rpc.on('workspace.open', (p) => {
      if (p.dir === 'D:\\B' && !p.gamePath) throw new RpcError('The game folder moved.', 'GAME_NOT_FOUND', 'PICK_GAME_FOLDER');
      return workspaceStatus({ dir: p.dir, gameRoot: p.gamePath ?? 'G:\\PK' });
    });
    await session.openWorkspace('D:\\A');
    await session.openWorkspace('D:\\B');
    platform.folders.push('G:\\Moved');

    await session.applyFix('PICK_GAME_FOLDER');

    expect(rpc.callsTo('workspace.open').at(-1)?.params).toEqual({ dir: 'D:\\B', gamePath: 'G:\\Moved' });
    expect(session.workspace?.dir).toBe('D:\\B');
  });

  it('PICK_WORKSPACE_FOLDER after a failed new workspace creates one in the picked folder', async () => {
    const { rpc, platform, session } = setup();
    session.install = installInfo();
    rpc.on('workspace.create', (p) => {
      if (p.dir === 'G:\\PK\\ws') throw new RpcError('Inside the game folder.', 'WORKSPACE_IN_GAME_FOLDER', 'PICK_WORKSPACE_FOLDER');
      return workspaceStatus({ dir: p.dir });
    });
    await session.createWorkspace('G:\\PK\\ws');
    platform.folders.push('D:\\ws');

    await session.applyFix('PICK_WORKSPACE_FOLDER');

    expect(rpc.callsTo('workspace.create').at(-1)?.params).toEqual({ dir: 'D:\\ws', gamePath: 'G:\\PK' });
    expect(rpc.callsTo('workspace.open')).toHaveLength(0);
    expect(session.workspace?.dir).toBe('D:\\ws');
  });

  it('keeps the last workspace when the core is not available at start', async () => {
    const { rpc, store, session } = setup({ 'tyrant.lastWorkspace': 'D:\\ws' });
    rpc.on('workspace.open', () => {
      throw new RpcError('The Tyrant core is restarting.', 'SIDECAR_UNAVAILABLE');
    });
    rpc.on('install.detect', () => {
      throw new RpcError('The Tyrant core is restarting.', 'SIDECAR_UNAVAILABLE');
    });

    await session.start();

    expect(store.get('tyrant.lastWorkspace')).toBe('D:\\ws');
  });

  it('quietly keeps an earlier error and reports only its own failure', async () => {
    const { session } = setup();
    session.error = new RpcError('No dump arrived.', 'DUMP_TIMEOUT');

    expect(await session.quietly(async () => 1)).toBe(1);
    expect(session.error?.code).toBe('DUMP_TIMEOUT');
    expect(
      await session.quietly(async () => {
        throw new RpcError('No data.', 'DATA_MISSING');
      }),
    ).toBeNull();
    expect(session.error?.code).toBe('DATA_MISSING');
  });

  it('opens the workspace preview folder to the app', async () => {
    const { rpc, platform, session } = setup();
    rpc.on('workspace.open', (p) => workspaceStatus({ dir: p.dir }));

    await session.openWorkspace('D:\\ws');

    expect(platform.allowed).toEqual(['D:\\ws\\cache\\previews']);
  });

  it('picks up a job that is still running after the window reloads', async () => {
    const { rpc, session } = setup({ 'tyrant.lastWorkspace': 'D:\\ws' });
    rpc.on('workspace.open', () => workspaceStatus());
    rpc.on('workspace.status', () => workspaceStatus());
    rpc.on('job.current', () => ({ job: { jobId: 'j7', title: 'Decompile code', fraction: 0.4, message: 'Decompiling Assembly-CSharp' } }));
    let finish!: (value: unknown) => void;
    rpc.onAttach('j7', () => new Promise((resolve) => (finish = resolve)));

    await session.start();

    await waitFor(() => expect(session.job?.title).toBe('Decompile code'));
    expect(session.job?.fraction).toBe(0.4);
    expect(session.job?.cancel).not.toBeNull();
    finish({});
    await waitFor(() => expect(session.job).toBeNull());
    expect(messages(session, null).at(-1)).toContain('Decompile code');
  });

  it("logs a job's progress once per new message to the tab that started it", async () => {
    const { rpc, session } = setup();
    session.workspace = workspaceStatus();
    rpc.on('decompile.run', (_p, onProgress) => {
      onProgress?.(0.1, 'Reading');
      onProgress?.(0.2, 'Reading');
      onProgress?.(0.5, 'Writing');
      return { assemblies: [], failed: [] };
    });
    rpc.on('workspace.status', () => workspaceStatus());
    const tab = testTab(session);

    await session.runJob('decompile.run', {}, 'Decompile', tab);

    expect(messages(session, tab)).toEqual(['Decompile started.', 'Reading', 'Writing']);
  });

  it('a cancelled job is an info record in its tab, not an error', async () => {
    const { rpc, session } = setup();
    rpc.on('decompile.run', () => {
      throw new RpcError('Cancelled.', 'CANCELLED');
    });
    rpc.on('workspace.status', () => workspaceStatus());
    session.workspace = workspaceStatus();
    const tab = testTab(session);

    await session.runJob('decompile.run', {}, 'Decompile', tab);

    expect(session.error).toBeNull();
    expect(tab.error).toBeNull();
    expect(messages(session, tab)).toContain('Decompile was cancelled.');
  });

  it('a failed job reports to its tab, not the session', async () => {
    const { rpc, session } = setup();
    rpc.on('assets.index', () => {
      throw new RpcError('The game folder is gone.', 'GAME_NOT_FOUND', 'PICK_GAME_FOLDER');
    });
    rpc.on('workspace.status', () => workspaceStatus());
    session.workspace = workspaceStatus();
    const tab = testTab(session);

    await session.runJob('assets.index', undefined, 'Asset index', tab);

    expect(session.error).toBeNull();
    expect(tab.error?.code).toBe('GAME_NOT_FOUND');
  });

  it('REINDEX_ASSETS indexes the assets without refreshing everything', async () => {
    const { rpc, session } = setup();
    rpc.on('assets.index', () => ({ assets: 1, failures: 0, missingBundles: 0 }));
    rpc.on('workspace.status', () => workspaceStatus());
    session.workspace = workspaceStatus();
    const tab = testTab(session);

    await session.applyFix('REINDEX_ASSETS', tab);

    expect(rpc.callsTo('assets.index')).toHaveLength(1);
    expect(rpc.callsTo('workspace.refreshAll')).toHaveLength(0);
  });

  it('says when previews cannot be allowed', async () => {
    const { rpc, platform, session } = setup();
    platform.previewsError = new Error('scope refused');
    rpc.on('workspace.open', () => workspaceStatus());

    await session.openWorkspace('D:\\ws');
    await flush();

    expect(session.log.records.some((r) => r.level === 'warn' && r.message.startsWith('Previews will not load: '))).toBe(true);
  });

  it('core log lines become records for every tab', () => {
    const { rpc, session } = setup();
    rpc.emitLog({ level: 'warn', message: 'The workspace now points at G:\\PK.' });
    expect(session.log.records[0]).toMatchObject({ tab: null, level: 'warn', message: 'The workspace now points at G:\\PK.' });
  });

  it('a core restart keeps the log and reports once', () => {
    const { rpc, session } = setup();
    session.log.add({ level: 'info', message: 'before', tab: 'tab-1' });
    rpc.emitExit({ code: 1, restarting: true, error: null });
    expect(session.error?.code).toBe('SIDECAR_EXITED');
    expect(session.log.records.map((r) => r.message)).toEqual(['before']);
  });

  it('a counting progress message is logged once, not every second', async () => {
    const { rpc, platform, store } = setup();
    const session = new Session(rpc, platform, store, () => 0);
    session.workspace = workspaceStatus();
    rpc.on('dump.run', (_p, onProgress) => {
      for (let s = 1; s <= 5; s++) onProgress?.(s / 10, `Waiting for the game (${s} s)`);
      return { objects: 1, types: 1, languages: 1, errors: [] } as never;
    });
    rpc.on('workspace.status', () => workspaceStatus());
    const tab = testTab(session);

    await session.runJob('dump.run', { timeoutSeconds: 60 }, 'Data dump', tab);

    expect(messages(session, tab)).toEqual(['Data dump started.', 'Waiting for the game (1 s)']);
  });

  it('a burst of progress messages is thinned to about one a second, keeping the last', async () => {
    const { rpc, platform, store } = setup();
    let now = 0;
    const session = new Session(rpc, platform, store, () => now);
    session.workspace = workspaceStatus();
    rpc.on('assets.export', (_p, onProgress) => {
      onProgress?.(0.1, 'Exporting T_A');
      onProgress?.(0.2, 'Exporting T_B'); // same moment: held back
      onProgress?.(0.3, 'Exporting T_C'); // replaces T_B
      now = 1500;
      onProgress?.(0.9, 'Exporting T_D'); // a second later: logged
      return { exported: 4, failed: 0, reportPath: 'r', failures: [] } as never;
    });
    rpc.on('workspace.status', () => workspaceStatus());
    const tab = testTab(session);

    await session.runJob('assets.export', { refs: [] }, 'Export assets', tab);

    expect(messages(session, tab)).toEqual(['Export assets started.', 'Exporting T_A', 'Exporting T_D']);
  });

  it('the last held-back progress message is logged when the job ends', async () => {
    const { rpc, platform, store } = setup();
    const session = new Session(rpc, platform, store, () => 0);
    session.workspace = workspaceStatus();
    rpc.on('assets.export', (_p, onProgress) => {
      onProgress?.(0.1, 'Exporting T_A');
      onProgress?.(0.9, 'Writing the report');
      return { exported: 1, failed: 0, reportPath: 'r', failures: [] } as never;
    });
    rpc.on('workspace.status', () => workspaceStatus());
    const tab = testTab(session);

    await session.runJob('assets.export', { refs: [] }, 'Export assets', tab);

    expect(messages(session, tab)).toEqual(['Export assets started.', 'Exporting T_A', 'Writing the report']);
  });
});
