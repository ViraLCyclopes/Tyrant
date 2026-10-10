import { tick } from 'svelte';
import { describe, expect, it } from 'vitest';
import type { ModTexturesResult } from '$lib/rpc/types.gen';
import { Tab } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import { ModDoc } from './modDoc.svelte';
import ModDetailsPage from './ModDetailsPage.svelte';

const result = (totalBytes: number): ModTexturesResult => ({
  compression: 'auto',
  textures: [{ file: 'a.png', kind: 'colour', mode: 'auto', override: false, format: 'DXT1', width: 4, height: 4, bytes: totalBytes }],
  totalBytes,
});

async function setup() {
  const pending: ((r: ModTexturesResult) => void)[] = [];
  const rpc = new FakeRpc()
    .on('mods.get', () => modDetail())
    .on('mods.check', () => ({ errors: [], warnings: [], missingCutouts: [] }))
    .on('mods.textures', () => new Promise<ModTexturesResult>((resolve) => pending.push(resolve)));
  const session = new Session(rpc, new FakePlatform(), memoryStore());
  const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
  const doc = new ModDoc('red-spot', rpc, tab, () => {});
  await doc.load();
  return { rpc, session, tab, doc, pending };
}

describe('textureFacts', () => {
  it('two pages on one doc share one mods.textures call per revision', async () => {
    const { rpc, session, tab, doc, pending } = await setup();
    renderWith(ModDetailsPage, session, { doc }, tab);
    renderWith(ModDetailsPage, session, { doc }, tab);
    await tick();
    expect(rpc.callsTo('mods.textures')).toHaveLength(1);

    pending[0](result(100));
    await tick();
    doc.detail = { ...doc.detail!, revision: 'r2' };
    await tick();
    expect(rpc.callsTo('mods.textures')).toHaveLength(2);
  });

  it('drops an answer that arrives after a newer one', async () => {
    const { rpc, session, tab, doc, pending } = await setup();
    renderWith(ModDetailsPage, session, { doc }, tab);
    await tick();
    doc.detail = { ...doc.detail!, revision: 'r2' };
    await tick();
    expect(rpc.callsTo('mods.textures')).toHaveLength(2);

    pending[1](result(222)); // the newer answer first
    await tick();
    pending[0](result(111)); // then the older one
    await tick();

    const { textureFacts } = await import('./textureFacts.svelte');
    expect(textureFacts(session, doc).current?.totalBytes).toBe(222);
  });
});
