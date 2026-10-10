import { describe, expect, it } from 'vitest';
import { describe as describeTexture, formatBytes } from './textures';

describe('texture sizes', () => {
  it('shows megabytes with one decimal below 10 and whole above', () => {
    expect(formatBytes(11_184_808)).toBe('11 MB');
    expect(formatBytes(2_796_200)).toBe('2.7 MB');
    expect(formatBytes(43_688)).toBe('0.04 MB');
  });

  it('names the format and the in-game size', () => {
    expect(describeTexture({ file: 'a.png', kind: 'colour', mode: 'auto', override: false, format: 'DXT1', width: 4096, height: 4096, bytes: 11_184_808 })).toBe('DXT1 · 11 MB in game');
  });
});
