import { ShaderStore } from '@babylonjs/core';
import '@babylonjs/core/Shaders/pbr.fragment';
import { describe, expect, it } from 'vitest';
import { PkAnimalPlugin } from './pkAnimalPlugin';

// The GLSL cannot run here; these pin it against Babylon's real PBR fragment source, which is where a mistake would break
// every animal preview in the app.
const source: string = ShaderStore.ShadersStore['pbrPixelShader'];
const code = PkAnimalPlugin.prototype.getCustomCode.call({}, 'fragment') as Record<string, string>;

describe('PkAnimalPlugin', () => {
  it('uses the ambient occlusion result only after the shader has declared it', () => {
    const declared = source.indexOf('ambientOcclusionOutParams aoOut;');
    expect(declared).toBeGreaterThan(0);
    const users = Object.entries(code).filter(([, glsl]) => glsl.includes('aoOut'));
    expect(users.length).toBeGreaterThan(0);
    for (const [point] of users) {
      const at = point.startsWith('!') ? source.search(new RegExp(point.slice(1))) : source.indexOf(`#define ${point}`);
      expect(at, point).toBeGreaterThan(declared);
    }
  });

  it('reads its maps only where the shader has texture coordinates', () => {
    for (const [point, glsl] of Object.entries(code))
      if (glsl.includes('vMainUV1')) expect(glsl, point).toContain('defined(MAINUV1)');
  });

  it('a map that failed to load does not keep the animal from being drawn', () => {
    const failed = { isReady: () => false, isReadyOrNotBlocking: () => true };
    const ready = PkAnimalPlugin.prototype.isReadyForSubMesh.call({ extra: failed, pattern: null } as never);
    expect(ready).toBe(true);
  });
});
