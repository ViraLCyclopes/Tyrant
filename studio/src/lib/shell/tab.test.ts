import { describe, expect, it } from 'vitest';
import { RpcError } from '$lib/rpc/client';
import { LogStore } from './logStore.svelte';
import { Tab, type TabHost } from './tab.svelte';

const host: TabHost = { retitle: () => {}, openTool: () => null };

describe('Tab', () => {
  it('logs info and warnings under its own id', () => {
    const log = new LogStore();
    const tab = new Tab('tab-1', log, host);
    tab.info('Installed.');
    tab.warn('Careful.', 'detail');
    expect(log.records.map((r) => [r.tab, r.level, r.message])).toEqual([
      ['tab-1', 'info', 'Installed.'],
      ['tab-1', 'warn', 'Careful.'],
    ]);
  });

  it('an actionable error becomes the tab banner and a log record', async () => {
    const log = new LogStore();
    const tab = new Tab('tab-1', log, host);
    const result = await tab.safely(async () => {
      throw new RpcError('No asset index.', 'ASSET_INDEX_MISSING', 'REFRESH_WORKSPACE');
    });
    expect(result).toBeNull();
    expect(tab.error?.code).toBe('ASSET_INDEX_MISSING');
    expect(log.records[0]).toMatchObject({ tab: 'tab-1', level: 'error', message: 'No asset index.', detail: 'ASSET_INDEX_MISSING' });
  });

  it('an error without a fix only goes to the log', async () => {
    const log = new LogStore();
    const tab = new Tab('tab-1', log, host);
    await tab.safely(async () => {
      throw new RpcError('Bad PNG.', 'MOD_INVALID');
    });
    expect(tab.error).toBeNull();
    expect(log.records[0].level).toBe('error');
  });

  it('safely clears the previous banner; quietly keeps it', async () => {
    const log = new LogStore();
    const tab = new Tab('tab-1', log, host);
    tab.error = new RpcError('Old.', 'X', 'REFRESH_WORKSPACE');
    await tab.quietly(async () => 1);
    expect(tab.error?.message).toBe('Old.');
    await tab.safely(async () => 1);
    expect(tab.error).toBeNull();
  });

  it('after detach, records are core-wide and no banner is set', async () => {
    const log = new LogStore();
    const tab = new Tab('tab-1', log, host);
    tab.detach();
    tab.info('Export finished.');
    await tab.safely(async () => {
      throw new RpcError('Late failure.', 'X', 'REFRESH_WORKSPACE');
    });
    expect(log.records.map((r) => r.tab)).toEqual([null, null]);
    expect(tab.error).toBeNull();
  });

  it('asks its host to retitle and open tools', () => {
    const calls: string[] = [];
    const tab = new Tab('tab-1', new LogStore(), { retitle: (id, t) => calls.push(`${id}:${t}`), openTool: (id) => (calls.push(id), 'tab-2') });
    tab.setTitle('Carch pack');
    expect(tab.openTool('assets')).toBe('tab-2');
    expect(calls).toEqual(['tab-1:Carch pack', 'assets']);
  });
});
