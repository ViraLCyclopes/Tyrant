import { describe, expect, it } from 'vitest';
import { RpcError } from '$lib/rpc/client';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { installInfo, workspaceStatus } from '$lib/test/fixtures';
import { Session, START_GAME_WARNING } from './session.svelte';

const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

function setup(stored: Record<string, string> = {}) {
  const rpc = new FakeRpc();
  const platform = new FakePlatform();
  const store = memoryStore(stored);
  return { rpc, platform, store, session: new Session(rpc, platform, store) };
}

describe('Session', () => {
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

  it('a cancelled job shows a notice, not an error', async () => {
    const { rpc, session } = setup();
    rpc.on('decompile.run', () => {
      throw new RpcError('Cancelled.', 'CANCELLED');
    });

    await session.runJob('decompile.run', {}, 'Decompile');

    expect(session.error).toBeNull();
    expect(session.notice).toBe('Decompile was cancelled.');
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
    expect(session.notice).toMatch(/copied/);
  });
});
