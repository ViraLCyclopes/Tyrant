import { describe, expect, it, vi } from 'vitest';
import { RpcError } from '$lib/rpc/client';
import { LogStore } from '$lib/shell/logStore.svelte';
import { Tab } from '$lib/shell/tab.svelte';
import { FakeRpc } from '$lib/test/fakeRpc';
import { modDetail } from '$lib/test/modFixtures';
import { ModDoc } from './modDoc.svelte';

function setup() {
  const rpc = new FakeRpc();
  const log = new LogStore();
  const tab = new Tab('tab-1', log, { retitle: () => {}, openTool: () => null });
  const timers: (() => void)[] = [];
  const doc = new ModDoc('red-spot', rpc, tab, (fn) => void timers.push(fn));
  rpc.on('mods.check', () => ({ errors: [], warnings: ['red-spot/blue male diffuse: small'], missingCutouts: [] }));
  return { rpc, log, tab, doc, timers };
}

describe('ModDoc', () => {
  it('loads the mod and checks it', async () => {
    const { rpc, doc, timers } = setup();
    rpc.on('mods.get', () => modDetail());
    await doc.load();
    timers.forEach((run) => run());
    await vi.waitFor(() => expect(doc.check?.warnings).toHaveLength(1));
    expect(doc.detail?.name).toBe('Carch pack');
  });

  it('an edit sends the revision, takes the new mod and adds an undo step', async () => {
    const { rpc, doc } = setup();
    rpc.on('mods.get', () => modDetail());
    rpc.on('mods.setDetails', () => modDetail({ name: 'Red spots', revision: 'r2', manifestJson: '{"name":"Red spots"}' }));
    await doc.load();

    expect(await doc.edit('mods.setDetails', { name: 'Red spots', version: '1.0.0' })).toBe(true);

    expect(rpc.callsTo('mods.setDetails')[0].params).toMatchObject({ id: 'red-spot', revision: 'r1', name: 'Red spots' });
    expect(doc.detail?.name).toBe('Red spots');
    expect(doc.canUndo).toBe(true);
    expect(doc.canRedo).toBe(false);
  });

  it('undo puts the previous manifest back and redo the next one', async () => {
    const { rpc, doc } = setup();
    rpc.on('mods.get', () => modDetail());
    rpc.on('mods.setDetails', () => modDetail({ name: 'Red spots', revision: 'r2', manifestJson: '{"name":"Red spots"}' }));
    rpc.on('mods.saveManifest', (p) =>
      modDetail(p.manifest.includes('spots') ? { name: 'Red spots', revision: 'r4', manifestJson: p.manifest } : { revision: 'r3', manifestJson: p.manifest }),
    );
    await doc.load();
    await doc.edit('mods.setDetails', { name: 'Red spots', version: '1.0.0' });

    await doc.undo();
    expect(rpc.callsTo('mods.saveManifest')[0].params).toEqual({ id: 'red-spot', revision: 'r2', manifest: '{"name":"Carch pack"}' });
    expect(doc.detail?.name).toBe('Carch pack');
    expect(doc.canRedo).toBe(true);

    await doc.redo();
    expect(rpc.callsTo('mods.saveManifest')[1].params).toEqual({ id: 'red-spot', revision: 'r3', manifest: '{"name":"Red spots"}' });
    expect(doc.detail?.name).toBe('Red spots');
    expect(doc.canUndo).toBe(true);
  });

  it('a failed edit keeps the mod and adds no undo step', async () => {
    const { rpc, doc, log } = setup();
    rpc.on('mods.get', () => modDetail());
    rpc.on('mods.renameSkin', () => {
      throw new RpcError('Another skin is already called Green.', 'MOD_INVALID');
    });
    await doc.load();

    expect(await doc.edit('mods.renameSkin', { skin: 'blue', name: 'Green' })).toBe(false);

    expect(doc.canUndo).toBe(false);
    expect(log.records.at(-1)?.message).toContain('already called Green');
  });

  it('an outside change reloads the mod and clears undo', async () => {
    const { rpc, doc, log } = setup();
    let revision = 'r1';
    rpc.on('mods.get', () => modDetail({ revision, name: revision === 'r1' ? 'Carch pack' : 'Edited elsewhere' }));
    rpc.on('mods.setDetails', () => modDetail({ revision: 'r2', manifestJson: 'x' }));
    rpc.on('mods.renameSkin', () => {
      throw new RpcError("'red-spot' changed outside this editor.", 'MOD_CHANGED');
    });
    await doc.load();
    await doc.edit('mods.setDetails', { name: 'A', version: '1' });
    revision = 'r9';

    expect(await doc.edit('mods.renameSkin', { skin: 'blue', name: 'X' })).toBe(false);

    expect(doc.detail?.name).toBe('Edited elsewhere');
    expect(doc.canUndo).toBe(false);
    expect(log.records.some((r) => r.message.includes('changed outside this tab'))).toBe(true);
  });

  it('showing the tab again reloads only when mod.json changed', async () => {
    const { rpc, doc } = setup();
    let revision = 'r1';
    rpc.on('mods.get', () => modDetail({ revision }));
    await doc.load();
    await doc.refreshIfChanged();
    expect(doc.detail?.revision).toBe('r1');
    revision = 'r2';
    await doc.refreshIfChanged();
    expect(doc.detail?.revision).toBe('r2');
  });

  it('takes files changed outside (a model sent from Blender) and keeps undo when mod.json is the same', async () => {
    const { rpc, doc } = setup();
    let filesStamp = 'f1';
    rpc.on('mods.get', () => modDetail({ revision: 'r1', filesStamp }));
    rpc.on('mods.setDetails', () => modDetail({ revision: 'r2', filesStamp }));
    await doc.load();
    await doc.edit('mods.setDetails', { name: 'A', version: '1' });
    rpc.on('mods.get', () => modDetail({ revision: 'r2', filesStamp }));
    filesStamp = 'f2';

    await doc.refreshIfChanged();

    expect(doc.detail?.filesStamp).toBe('f2');
    expect(doc.canUndo).toBe(true);
  });

  it('checks once about a second after edits stop, dropping older results', async () => {
    const { rpc, doc, timers } = setup();
    rpc.on('mods.get', () => modDetail());
    rpc.on('mods.setDetails', () => modDetail({ revision: 'r2' }));
    await doc.load();
    timers.length = 0; // the load's own check
    await doc.edit('mods.setDetails', { name: 'A', version: '1' });
    await doc.edit('mods.setDetails', { name: 'B', version: '1' });
    const before = rpc.callsTo('mods.check').length;

    timers.forEach((run) => run()); // both scheduled checks fire; only the newest one asks
    await vi.waitFor(() => expect(rpc.callsTo('mods.check').length).toBe(before + 1));
  });

  it('two edits fired back to back go one after the other, the second with the newest revision', async () => {
    const { rpc, doc } = setup();
    rpc.on('mods.get', () => modDetail());
    let n = 1;
    rpc.on('mods.setDetails', async () => {
      await new Promise((r) => setTimeout(r, 5));
      n += 1;
      return modDetail({ revision: `r${n}`, manifestJson: `m${n}` });
    });
    await doc.load();

    await Promise.all([doc.edit('mods.setDetails', { name: 'A', version: '1' }), doc.edit('mods.setDetails', { name: 'B', version: '1' })]);

    expect(rpc.callsTo('mods.setDetails').map((c) => (c.params as { revision: string }).revision)).toEqual(['r1', 'r2']);
    expect(doc.detail?.revision).toBe('r3');
  });

  it('pressing undo twice quickly goes back two steps', async () => {
    const { rpc, doc } = setup();
    rpc.on('mods.get', () => modDetail({ revision: 'r1', manifestJson: 'm1' }));
    let n = 1;
    rpc.on('mods.setDetails', () => {
      n += 1;
      return modDetail({ revision: `r${n}`, manifestJson: `m${n}` });
    });
    rpc.on('mods.saveManifest', async (p) => {
      await new Promise((r) => setTimeout(r, 5));
      n += 1;
      return modDetail({ revision: `r${n}`, manifestJson: p.manifest });
    });
    await doc.load();
    await doc.edit('mods.setDetails', { name: 'A', version: '1' });
    await doc.edit('mods.setDetails', { name: 'B', version: '1' });

    await Promise.all([doc.undo(), doc.undo()]);

    expect(rpc.callsTo('mods.saveManifest').map((c) => (c.params as { manifest: string }).manifest)).toEqual(['m2', 'm1']);
    expect(doc.detail?.manifestJson).toBe('m1');
    expect(doc.canUndo).toBe(false);
    expect(doc.canRedo).toBe(true);
  });

  it('an edit can build its parameters from the mod as it is when its turn comes', async () => {
    const { rpc, doc } = setup();
    rpc.on('mods.get', () => modDetail());
    rpc.on('mods.setDetails', async (p) => {
      await new Promise((r) => setTimeout(r, 5));
      return modDetail({ name: p.name, revision: `r-${p.name}` });
    });
    await doc.load();

    await Promise.all([
      doc.edit('mods.setDetails', { name: 'A', version: '1' }),
      doc.edit('mods.setDetails', (d) => ({ name: `${d.name}+B`, version: '1' })),
    ]);

    expect(doc.detail?.name).toBe('A+B');
  });

  it('a reload that finds mod.json changed forgets undo', async () => {
    const { rpc, doc } = setup();
    let revision = 'r1';
    rpc.on('mods.get', () => modDetail({ revision }));
    rpc.on('mods.setDetails', () => modDetail({ revision: 'r2' }));
    await doc.load();
    await doc.edit('mods.setDetails', { name: 'A', version: '1' });
    revision = 'r7'; // e.g. Add skin or Replace file changed the files

    await doc.reload();

    expect(doc.detail?.revision).toBe('r7');
    expect(doc.canUndo).toBe(false);
  });

  it('an edit that cannot be undone forgets the history', async () => {
    const { rpc, doc } = setup();
    rpc.on('mods.get', () => modDetail());
    rpc.on('mods.setDetails', () => modDetail({ revision: 'r2' }));
    rpc.on('mods.removeSkin', () => modDetail({ revision: 'r3', skins: [] }));
    await doc.load();
    await doc.edit('mods.setDetails', { name: 'A', version: '1' });

    await doc.edit('mods.removeSkin', { skin: 'blue', deleteFiles: true }, { undoable: false });

    expect(doc.canUndo).toBe(false);
    expect(doc.canRedo).toBe(false);
  });
});
