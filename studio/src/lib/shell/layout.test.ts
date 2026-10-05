import { describe, expect, it } from 'vitest';
import logPanel from './LogPanel.svelte?raw';
import shell from './Shell.svelte?raw';

// jsdom has no layout engine; these pin the rules that keep the window from scrolling sideways (measured in Chrome: with the log
// at the side, its header of buttons was 524 px in a 360 px panel, the page grew wider than the window, and the window's
// scrollbar covered the status line).
const rule = (source: string, selector: string): string => source.match(new RegExp(`\\n\\s*${selector.replace('.', '\\.')}\\s*\\{([^}]*)\\}`))?.[1] ?? '';

describe('shell layout', () => {
  it('the log header wraps its buttons instead of growing past the panel', () => {
    expect(rule(logPanel, 'header')).toMatch(/flex-wrap:\s*wrap/);
  });

  it('nothing can make the window scroll sideways and cover the status line', () => {
    expect(rule(shell, '.shell')).toMatch(/overflow:\s*hidden/);
  });
});
