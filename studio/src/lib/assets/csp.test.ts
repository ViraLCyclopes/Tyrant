import { describe, expect, it } from 'vitest';
import conf from '../../../src-tauri/tauri.conf.json';

// The policy applies only to built apps (the dev server ignores it), so a missing source shows up first in Tyrant.exe.
const directive = (name: string): string[] =>
  conf.app.security.csp.split(';').map((d) => d.trim().split(/\s+/)).find((d) => d[0] === name)?.slice(1) ?? [];

describe('content security policy', () => {
  it('lets the 3D viewer show the textures a .glb links (Babylon decodes them from blob: URLs)', () => {
    expect(directive('img-src')).toContain('blob:');
  });

  it('lets previews load from the asset protocol', () => {
    expect(directive('img-src')).toContain('http://asset.localhost');
    expect(directive('connect-src')).toContain('http://asset.localhost');
  });
});
