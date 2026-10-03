import { describe, expect, it } from 'vitest';
import { attentionFor, panelSize, toLevel, visibleRecords, type LogRecord } from './log';
import { LogStore } from './logStore.svelte';

const rec = (tab: string | null, level: LogRecord['level'] = 'info', seq = 1): LogRecord => ({ seq, time: 0, level, message: 'm', tab });

describe('visibleRecords', () => {
  it("shows a tab's own records and every core record, nothing from other tabs", () => {
    const all = [rec('a'), rec('b'), rec(null)];
    expect(visibleRecords(all, 'a')).toEqual([all[0], all[2]]);
  });
});

describe('attentionFor', () => {
  it('marks a tab with its loudest problem and ignores core records', () => {
    const marks = attentionFor([rec('a', 'warn'), rec('a', 'error'), rec('a', 'warn'), rec('b', 'warn'), rec(null, 'error'), rec('c')]);
    expect(marks.get('a')).toBe('error');
    expect(marks.get('b')).toBe('warn');
    expect(marks.has('c')).toBe(false);
    expect(marks.size).toBe(2);
  });
});

describe('toLevel', () => {
  it('keeps known levels and treats anything else as info', () => {
    expect(toLevel('warn')).toBe('warn');
    expect(toLevel('error')).toBe('error');
    expect(toLevel('debug')).toBe('info');
  });
});

describe('LogStore', () => {
  it('numbers records and stamps their time', () => {
    const store = new LogStore(10, () => 42);
    const first = store.add({ level: 'info', message: 'one', tab: 'a' });
    const second = store.add({ level: 'warn', message: 'two', tab: null });
    expect([first.seq, second.seq]).toEqual([1, 2]);
    expect(first.time).toBe(42);
    expect(store.lastSeq()).toBe(2);
  });

  it("the cap drops only that tab's oldest records", () => {
    const store = new LogStore(3);
    store.add({ level: 'info', message: 'core', tab: null });
    for (let i = 1; i <= 5; i++) store.add({ level: 'info', message: `a${i}`, tab: 'a' });
    store.add({ level: 'info', message: 'b1', tab: 'b' });
    expect(store.forTab('a').map((r) => r.message)).toEqual(['core', 'a3', 'a4', 'a5']);
    expect(store.forTab('b').map((r) => r.message)).toEqual(['core', 'b1']);
  });

  it("clear removes only that tab's records", () => {
    const store = new LogStore();
    store.add({ level: 'info', message: 'a', tab: 'a' });
    store.add({ level: 'info', message: 'core', tab: null });
    store.clear('a');
    expect(store.records.map((r) => r.message)).toEqual(['core']);
  });

  it('tells listeners about each new record', () => {
    const store = new LogStore();
    const seen: string[] = [];
    const stop = store.onAdd((r) => seen.push(r.message));
    store.add({ level: 'info', message: 'x', tab: 'a' });
    stop();
    store.add({ level: 'info', message: 'y', tab: 'a' });
    expect(seen).toEqual(['x']);
  });
});

describe('panelSize', () => {
  it('follows the pointer at the bottom', () => {
    expect(panelSize({ from: 180, start: 700, now: 650, position: 'bottom', viewport: 1000 })).toBe(230);
  });

  it('follows the pointer at the side, where the panel is twice as wide as its size', () => {
    expect(panelSize({ from: 180, start: 1000, now: 900, position: 'side', viewport: 1600 })).toBe(230); // 100 px wider = 50 more
  });

  it('never covers more than 60 % of the window, and never shrinks below 80', () => {
    expect(panelSize({ from: 180, start: 1000, now: 0, position: 'side', viewport: 1600 })).toBe(480); // 960 px = 60 %
    expect(panelSize({ from: 180, start: 700, now: 1000, position: 'bottom', viewport: 1000 })).toBe(80);
  });
});
