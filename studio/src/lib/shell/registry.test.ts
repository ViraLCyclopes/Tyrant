import { describe, expect, it } from 'vitest';
import { Registry, type ToolDef } from './registry';

const load = async () => ({ default: (() => {}) as never });
const tool = (over: Partial<ToolDef> = {}): ToolDef => ({ id: 'assets', name: 'Assets', blurb: 'b', icon: '', status: 'ready', instances: 'many', load, ...over });

describe('Registry', () => {
  it('refuses a second tool with the same id', () => {
    const r = new Registry();
    r.register(tool());
    expect(() => r.register(tool())).toThrow(/already registered/);
  });

  it('only ready tools are openable', () => {
    const r = new Registry();
    r.register(tool());
    r.register(tool({ id: 'scripts', status: 'planned', load: undefined }));
    expect(r.isOpenable('assets')).toBe(true);
    expect(r.isOpenable('scripts')).toBe(false);
    expect(r.isOpenable('missing')).toBe(false);
  });

  it('the dock lists tools in order without Home or hidden ones', () => {
    const r = new Registry();
    r.register(tool({ id: 'home', inDock: false }));
    r.register(tool({ id: 'workspace' }));
    r.register(tool({ id: 'mod', inDock: false }));
    r.register(tool({ id: 'scripts', status: 'planned', load: undefined }));
    expect(r.dock().map((t) => t.id)).toEqual(['workspace', 'scripts']);
  });
});
