// Ported from Scute's ui/src/shell/tabs.test.ts (no log names; tab keys and cycling added).
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TabStore } from './tabs';

let tabs: TabStore;

beforeEach(() => {
  tabs = new TabStore();
});

describe('opening', () => {
  it('makes the first tab active', () => {
    const id = tabs.open({ toolId: 'home', title: 'Home', closable: false });
    expect(tabs.activeId()).toBe(id);
  });

  it('gives every tab a distinct id', () => {
    const a = tabs.open({ toolId: 'assets', title: 'A' });
    const b = tabs.open({ toolId: 'assets', title: 'A' });
    expect(a).not.toBe(b);
  });

  it('activates each newly opened tab', () => {
    tabs.open({ toolId: 'home', title: 'Home', closable: false });
    const second = tabs.open({ toolId: 'assets', title: 'Assets' });
    expect(tabs.activeId()).toBe(second);
  });
});

describe('closing', () => {
  it('activates the left neighbour', () => {
    const home = tabs.open({ toolId: 'home', title: 'Home', closable: false });
    const first = tabs.open({ toolId: 'assets', title: 'A' });
    const second = tabs.open({ toolId: 'assets', title: 'B' });
    tabs.close(second);
    expect(tabs.activeId()).toBe(first);
    expect(tabs.list().map((t) => t.id)).toEqual([home, first]);
  });

  it('falls back to the first remaining tab when nothing is to the left', () => {
    const home = tabs.open({ toolId: 'home', title: 'Home', closable: false });
    const only = tabs.open({ toolId: 'assets', title: 'A' });
    tabs.activate(only);
    tabs.close(only);
    expect(tabs.activeId()).toBe(home);
  });

  it('refuses to close a tab that is not closable', () => {
    const home = tabs.open({ toolId: 'home', title: 'Home', closable: false });
    expect(tabs.close(home)).toBe(false);
    expect(tabs.list()).toHaveLength(1);
    expect(tabs.activeId()).toBe(home);
  });

  it("leaves an inactive tab's activation alone", () => {
    tabs.open({ toolId: 'home', title: 'Home', closable: false });
    const first = tabs.open({ toolId: 'assets', title: 'A' });
    const second = tabs.open({ toolId: 'assets', title: 'B' });
    tabs.close(first);
    expect(tabs.activeId()).toBe(second);
  });

  it('activates the new leftmost tab when the active tab was itself leftmost', () => {
    const first = tabs.open({ toolId: 'assets', title: 'A' });
    const second = tabs.open({ toolId: 'assets', title: 'B' });
    tabs.activate(first);
    tabs.close(first);
    expect(tabs.list().map((t) => t.id)).toEqual([second]);
    expect(tabs.activeId()).toBe(second);
  });

  it('has no active tab once the last one closes', () => {
    const only = tabs.open({ toolId: 'assets', title: 'A' });
    expect(tabs.close(only)).toBe(true);
    expect(tabs.list()).toHaveLength(0);
    expect(tabs.activeId()).toBeNull();
  });
});

describe('duplicate titles', () => {
  it('suffixes the second tab with the same title', () => {
    const first = tabs.open({ toolId: 'assets', title: 'Assets' });
    const second = tabs.open({ toolId: 'assets', title: 'Assets' });
    expect(tabs.displayTitle(first)).toBe('Assets');
    expect(tabs.displayTitle(second)).toBe('Assets (2)');
  });

  it('does not suffix distinct titles', () => {
    const a = tabs.open({ toolId: 'assets', title: 'A' });
    const b = tabs.open({ toolId: 'assets', title: 'B' });
    expect(tabs.displayTitle(a)).toBe('A');
    expect(tabs.displayTitle(b)).toBe('B');
  });

  it('renumbers when an earlier duplicate closes', () => {
    const first = tabs.open({ toolId: 'assets', title: 'Assets' });
    const second = tabs.open({ toolId: 'assets', title: 'Assets' });
    tabs.close(first);
    expect(tabs.displayTitle(second)).toBe('Assets');
  });

  it('renumbers after a retitle makes two tabs collide', () => {
    const a = tabs.open({ toolId: 'assets', title: 'A' });
    const b = tabs.open({ toolId: 'assets', title: 'B' });
    tabs.retitle(b, 'A');
    expect(tabs.displayTitle(a)).toBe('A');
    expect(tabs.displayTitle(b)).toBe('A (2)');
  });
});

it('keeps a very long title intact in the model, leaving truncation to CSS', () => {
  const long = 'a'.repeat(300);
  const id = tabs.open({ toolId: 'assets', title: long });
  expect(tabs.displayTitle(id)).toBe(long);
});

describe('subscribers', () => {
  it('fires on open, activate, retitle and close', () => {
    const listener = vi.fn();
    const first = tabs.open({ toolId: 'assets', title: 'A' });
    tabs.subscribe(listener);
    const id = tabs.open({ toolId: 'assets', title: 'A' });
    tabs.retitle(id, 'B');
    tabs.activate(first); // activating the tab already in front changes nothing, so it does not fire
    tabs.close(id);
    expect(listener).toHaveBeenCalledTimes(4);
  });

  it('stops firing once unsubscribed', () => {
    const listener = vi.fn();
    const off = tabs.subscribe(listener);
    off();
    tabs.open({ toolId: 'assets', title: 'A' });
    expect(listener).not.toHaveBeenCalled();
  });
});

describe('keys and cycling', () => {
  it('stores a key for keyed tabs', () => {
    const id = tabs.open({ toolId: 'mod', title: 'Carch pack', key: 'carch-pack' });
    expect(tabs.get(id)?.key).toBe('carch-pack');
    expect(tabs.get(tabs.open({ toolId: 'assets', title: 'Assets' }))?.key).toBeNull();
  });

  it('cycles forwards and backwards, wrapping around', () => {
    const a = tabs.open({ toolId: 'home', title: 'Home', closable: false });
    const b = tabs.open({ toolId: 'assets', title: 'Assets' });
    const c = tabs.open({ toolId: 'data', title: 'Data' });
    tabs.cycle(1);
    expect(tabs.activeId()).toBe(a);
    tabs.cycle(-1);
    expect(tabs.activeId()).toBe(c);
    tabs.activate(a);
    tabs.cycle(1);
    expect(tabs.activeId()).toBe(b);
  });
});
