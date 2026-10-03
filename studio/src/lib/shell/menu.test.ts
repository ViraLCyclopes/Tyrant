import { describe, expect, it } from 'vitest';
import { isEnabled, itemsOf, type Menu } from './menu';

describe('menu model', () => {
  it('re-reads item lists given as a function each time', () => {
    let n = 1;
    const menu: Menu = { label: 'File', items: () => Array.from({ length: n }, (_, i) => ({ label: `R${i}`, run: () => {} })) };
    expect(itemsOf(menu)).toHaveLength(1);
    n = 3;
    expect(itemsOf(menu)).toHaveLength(3);
  });

  it('items are enabled unless they say otherwise; separators never are', () => {
    expect(isEnabled({ label: 'A', run: () => {} })).toBe(true);
    expect(isEnabled({ label: 'B', run: () => {}, enabled: () => false })).toBe(false);
    expect(isEnabled({ separator: true })).toBe(false);
  });
});
