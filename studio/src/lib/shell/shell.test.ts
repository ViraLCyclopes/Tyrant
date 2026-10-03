import { describe, expect, it } from 'vitest';
import { RpcError } from '$lib/rpc/client';
import { memoryStore, type KeyValueStore } from '$lib/storage';
import { LogStore } from './logStore.svelte';
import { Registry, type ToolDef } from './registry';
import { ShellState } from './shell.svelte';

const load = async () => ({ default: (() => {}) as never });

function shell(store: KeyValueStore = memoryStore()) {
  const registry = new Registry();
  const def = (over: Partial<ToolDef>): ToolDef => ({ id: 'x', name: 'X', blurb: '', icon: '', status: 'ready', instances: 'many', load, ...over });
  registry.register(def({ id: 'home', name: 'Home', instances: 'single', closable: false, inDock: false }));
  registry.register(def({ id: 'workspace', name: 'Workspace', instances: 'single' }));
  registry.register(def({ id: 'assets', name: 'Assets' }));
  registry.register(def({ id: 'scripts', name: 'Script mods', status: 'planned', load: undefined }));
  const log = new LogStore();
  const s = new ShellState(registry, log, store);
  s.start();
  return { s, log, store };
}

describe('ShellState', () => {
  it('starts with Home, which cannot close', () => {
    const { s } = shell();
    expect(s.tabs.map((t) => t.toolId)).toEqual(['home']);
    s.close(s.tabs[0].id);
    expect(s.tabs).toHaveLength(1);
  });

  it('a single tool brings its open tab to the front instead of opening another', () => {
    const { s } = shell();
    const first = s.openTool('workspace');
    s.activate(s.tabs[0].id);
    expect(s.openTool('workspace')).toBe(first);
    expect(s.activeId).toBe(first);
    expect(s.tabs.filter((t) => t.toolId === 'workspace')).toHaveLength(1);
  });

  it('a many tool opens a new tab each time; a keyed open finds its tab', () => {
    const { s } = shell();
    const a = s.openTool('assets');
    const b = s.openTool('assets');
    expect(a).not.toBe(b);
    const keyed = s.openTool('assets', { key: 'k', title: 'K' });
    expect(s.openTool('assets', { key: 'k' })).toBe(keyed);
  });

  it('planned and unknown tools do not open', () => {
    const { s } = shell();
    expect(s.openTool('scripts')).toBeNull();
    expect(s.openTool('nope')).toBeNull();
    expect(s.tabs).toHaveLength(1);
  });

  it('only the shown tab is active', () => {
    const { s } = shell();
    const a = s.openTool('assets')!;
    const home = s.tabs[0].id;
    expect(s.tab(a).active).toBe(true);
    expect(s.tab(home).active).toBe(false);
    s.activate(home);
    expect(s.tab(a).active).toBe(false);
    expect(s.tab(home).active).toBe(true);
  });

  it('closing the shown tab moves to its left neighbour', () => {
    const { s } = shell();
    const a = s.openTool('assets')!;
    const b = s.openTool('assets')!;
    s.close(b);
    expect(s.activeId).toBe(a);
  });

  it('marks a background tab that logged a warning, and clears it when shown', () => {
    const { s } = shell();
    const a = s.openTool('assets')!;
    s.activate(s.tabs[0].id);
    s.tab(a).warn('Careful.');
    expect(s.marker(a)).toBe('warn');
    s.activate(a);
    expect(s.marker(a)).toBeNull();
  });

  it('an error in the shown tab opens its log panel', () => {
    const { s } = shell();
    const a = s.openTool('assets')!;
    expect(s.panel(a).open).toBe(false);
    s.tab(a).fail(new RpcError('Broken.', 'X'));
    expect(s.panel(a).open).toBe(true);
  });

  it('a job whose tab was closed logs to every tab', () => {
    const { s, log } = shell();
    const a = s.openTool('assets')!;
    const tab = s.tab(a);
    s.close(a);
    tab.info('Export finished.');
    expect(log.records.at(-1)).toMatchObject({ tab: null, message: 'Export finished.' });
  });

  it('remembers open tabs and reopens them', () => {
    const store = memoryStore();
    const first = shell(store).s;
    first.openTool('workspace');
    first.openTool('assets', { title: 'Raptor textures' });
    first.save();
    const { s } = shell(store);
    expect(s.tabs.map((t) => [t.toolId, t.title])).toEqual([
      ['home', 'Home'],
      ['workspace', 'Workspace'],
      ['assets', 'Raptor textures'],
    ]);
    expect(s.tab(s.activeId!).active).toBe(true);
    expect(s.tabs.find((t) => t.id === s.activeId)?.toolId).toBe('assets');
  });

  it('restoring skips unknown and planned tools', () => {
    const store = memoryStore({
      'tyrant.shell.tabs': JSON.stringify({
        tabs: [
          { toolId: 'gone', title: 'Gone', key: null },
          { toolId: 'scripts', title: 'S', key: null },
          { toolId: 'assets', title: 'Assets', key: null },
        ],
        active: 5,
      }),
    });
    const { s } = shell(store);
    expect(s.tabs.map((t) => t.toolId)).toEqual(['home', 'assets']);
  });

  it('restoring survives corrupt storage', () => {
    const { s } = shell(memoryStore({ 'tyrant.shell.tabs': '{nope', 'tyrant.shell.prefs': '[1,2' }));
    expect(s.tabs.map((t) => t.toolId)).toEqual(['home']);
    expect(s.prefs).toEqual({ reopenTabs: true, logPosition: 'bottom' });
  });

  it('does not reopen tabs when the preference is off', () => {
    const store = memoryStore();
    const first = shell(store).s;
    first.openTool('assets');
    first.setPrefs({ reopenTabs: false });
    first.save();
    expect(shell(store).s.tabs.map((t) => t.toolId)).toEqual(['home']);
  });
});
