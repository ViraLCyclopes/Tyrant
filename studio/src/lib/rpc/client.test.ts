import { describe, expect, it } from 'vitest';
import { RpcClient, RpcError, type ExitInfo, type Transport } from './client';

class FakeTransport implements Transport {
  sent: { id: number; method: string; params?: unknown }[] = [];
  failSend: Error | null = null;
  private lineHandlers: ((line: string) => void)[] = [];
  private exitHandlers: ((info: ExitInfo) => void)[] = [];
  private startedHandlers: (() => void)[] = [];

  async send(line: string): Promise<void> {
    if (this.failSend) throw this.failSend;
    this.sent.push(JSON.parse(line));
  }
  onLine(handler: (line: string) => void) { this.lineHandlers.push(handler); }
  onExit(handler: (info: ExitInfo) => void) { this.exitHandlers.push(handler); }
  onStarted(handler: () => void) { this.startedHandlers.push(handler); }

  emit(message: unknown) { this.lineHandlers.forEach((h) => h(JSON.stringify(message))); }
  emitRaw(line: string) { this.lineHandlers.forEach((h) => h(line)); }
  exit(info: ExitInfo) { this.exitHandlers.forEach((h) => h(info)); }
  started() { this.startedHandlers.forEach((h) => h()); }
  last() { return this.sent[this.sent.length - 1]; }
}

const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

function setup() {
  const transport = new FakeTransport();
  return { transport, client: new RpcClient(transport) };
}

describe('RpcClient', () => {
  it('sends JSON-RPC 2.0 requests and resolves the matching response', async () => {
    const { transport, client } = setup();
    const result = client.call('install.detect', { gamePath: 'G:\\Game' });
    await flush();

    expect(transport.last()).toEqual({ jsonrpc: '2.0', id: 1, method: 'install.detect', params: { gamePath: 'G:\\Game' } });
    transport.emit({ jsonrpc: '2.0', id: 1, result: { rootDir: 'G:\\Game', steamAppId: null, buildGuid: 'b1' } });
    await expect(result).resolves.toEqual({ rootDir: 'G:\\Game', steamAppId: null, buildGuid: 'b1' });
  });

  it('leaves params out for methods without them', async () => {
    const { transport, client } = setup();
    void client.call('workspace.status');
    await flush();

    expect(transport.last()).toEqual({ jsonrpc: '2.0', id: 1, method: 'workspace.status' });
  });

  it('resolves concurrent calls by id, in any order', async () => {
    const { transport, client } = setup();
    const first = client.call('app.info');
    const second = client.call('app.diagnostics');
    await flush();

    transport.emit({ jsonrpc: '2.0', id: 2, result: { text: 'diag' } });
    transport.emit({ jsonrpc: '2.0', id: 1, result: { version: '0.1.0', protocolVersion: 1, runtime: '.NET 10' } });

    await expect(second).resolves.toEqual({ text: 'diag' });
    await expect(first).resolves.toMatchObject({ protocolVersion: 1 });
  });

  it('turns error responses into RpcError with code and fix', async () => {
    const { transport, client } = setup();
    const result = client.call('workspace.status');
    await flush();
    transport.emit({ jsonrpc: '2.0', id: 1, error: { code: -32000, message: 'No workspace is open.', data: { code: 'WORKSPACE_INVALID', fix: 'PICK_WORKSPACE_FOLDER' } } });

    const error = await result.catch((e: unknown) => e);
    expect(error).toBeInstanceOf(RpcError);
    expect(error).toMatchObject({ message: 'No workspace is open.', code: 'WORKSPACE_INVALID', fix: 'PICK_WORKSPACE_FOLDER' });
  });

  it('names JSON-RPC errors that carry no data', async () => {
    const { transport, client } = setup();
    const result = client.call('app.info');
    await flush();
    transport.emit({ jsonrpc: '2.0', id: 1, error: { code: -32601, message: 'Method not found: app.info.', data: null } });

    await expect(result).rejects.toMatchObject({ code: 'METHOD_NOT_FOUND', fix: null });
  });

  it('runs a job: progress, then its result', async () => {
    const { transport, client } = setup();
    const progress: [number, string][] = [];
    const started = client.job('dump.run', { timeoutSeconds: 300 }, (fraction, message) => progress.push([fraction, message]));
    await flush();
    transport.emit({ jsonrpc: '2.0', id: 1, result: { jobId: 'j1' } });
    const handle = await started;

    transport.emit({ jsonrpc: '2.0', method: 'job.progress', params: { jobId: 'j1', fraction: 0.5, message: 'Waiting for the game' } });
    transport.emit({ jsonrpc: '2.0', method: 'job.done', params: { jobId: 'j1', result: { objects: 3, types: 1, languages: 0, errors: [] } } });

    expect(handle.id).toBe('j1');
    await expect(handle.done).resolves.toEqual({ objects: 3, types: 1, languages: 0, errors: [] });
    expect(progress).toEqual([[0.5, 'Waiting for the game']]);
  });

  it('delivers job notifications that arrive before the job id', async () => {
    const { transport, client } = setup();
    const progress: number[] = [];
    const started = client.job('assets.index', undefined, (fraction) => progress.push(fraction));
    await flush();
    transport.emit({ jsonrpc: '2.0', method: 'job.progress', params: { jobId: 'j9', fraction: 0.2, message: 'x' } });
    transport.emit({ jsonrpc: '2.0', method: 'job.done', params: { jobId: 'j9', result: { assets: 5, failures: 0, missingBundles: 0 } } });
    transport.emit({ jsonrpc: '2.0', id: 1, result: { jobId: 'j9' } });

    const handle = await started;
    await expect(handle.done).resolves.toEqual({ assets: 5, failures: 0, missingBundles: 0 });
    expect(progress).toEqual([0.2]);
  });

  it('rejects a failed job with its error code', async () => {
    const { transport, client } = setup();
    const started = client.job('dump.run', {});
    await flush();
    transport.emit({ jsonrpc: '2.0', id: 1, result: { jobId: 'j1' } });
    const handle = await started;
    transport.emit({ jsonrpc: '2.0', method: 'job.failed', params: { jobId: 'j1', error: { code: -32000, message: 'Not installed.', data: { code: 'DUMPER_NOT_INSTALLED', fix: 'INSTALL_DUMPER' } } } });

    await expect(handle.done).rejects.toMatchObject({ code: 'DUMPER_NOT_INSTALLED', fix: 'INSTALL_DUMPER' });
  });

  it('cancel sends job.cancel for that job', async () => {
    const { transport, client } = setup();
    const started = client.job('decompile.run', {});
    await flush();
    transport.emit({ jsonrpc: '2.0', id: 1, result: { jobId: 'j1' } });
    const handle = await started;

    const cancelled = handle.cancel();
    await flush();
    expect(transport.last()).toEqual({ jsonrpc: '2.0', id: 2, method: 'job.cancel', params: { jobId: 'j1' } });
    transport.emit({ jsonrpc: '2.0', id: 2, result: { cancelled: true } });
    await cancelled;
    handle.done.catch(() => {});
  });

  it('rejects everything in flight when the core exits, and tells listeners', async () => {
    const { transport, client } = setup();
    const exits: ExitInfo[] = [];
    client.onCoreExit((info) => exits.push(info));
    const call = client.call('workspace.status');
    const started = client.job('dump.run', {});
    await flush();
    transport.emit({ jsonrpc: '2.0', id: 2, result: { jobId: 'j1' } });
    const handle = await started;

    transport.exit({ code: -1, restarting: true, error: null });

    await expect(call).rejects.toMatchObject({ code: 'SIDECAR_EXITED' });
    await expect(handle.done).rejects.toMatchObject({ code: 'SIDECAR_EXITED' });
    expect(exits).toEqual([{ code: -1, restarting: true, error: null }]);
  });

  it('uses the shell message when the core could not start', async () => {
    const { transport, client } = setup();
    const call = client.call('app.info');
    await flush();
    transport.exit({ code: null, restarting: false, error: 'Could not start the Tyrant core (C:\\x\\tyrant.exe): not found.' });

    await expect(call).rejects.toMatchObject({ code: 'SIDECAR_EXITED', message: 'Could not start the Tyrant core (C:\\x\\tyrant.exe): not found.' });
  });

  it('reports an unavailable core when sending fails', async () => {
    const { transport, client } = setup();
    transport.failSend = new Error('The Tyrant core is restarting; try again in a moment.');

    await expect(client.call('app.info')).rejects.toMatchObject({ code: 'SIDECAR_UNAVAILABLE' });
  });

  it('tells listeners when the core (re)starts', () => {
    const { transport, client } = setup();
    let starts = 0;
    client.onCoreStarted(() => starts++);
    transport.started();
    expect(starts).toBe(1);
  });

  it('ignores lines that are not JSON or not for it', async () => {
    const { transport, client } = setup();
    const call = client.call('app.info');
    await flush();
    transport.emitRaw('not json');
    transport.emit({ jsonrpc: '2.0', id: 99, result: {} });
    transport.emit({ jsonrpc: '2.0', id: 1, result: { version: '0.1.0', protocolVersion: 1, runtime: 'x' } });

    await expect(call).resolves.toMatchObject({ version: '0.1.0' });
  });
});
