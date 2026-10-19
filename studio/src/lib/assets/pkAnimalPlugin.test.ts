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

  it('adds iridescence where the shader has both the normal and the view direction, outside any function', () => {
    const glsl = Object.entries(code).filter(([, g]) => g.includes('pkRampSampler') && g.includes('normalW'));
    expect(glsl.length).toBe(1);
    const [point, term] = glsl[0];
    expect(term).toContain('pow(1.0 - nv');
    expect(term).toContain('#ifdef PK_IRID');
    const at = source.indexOf(`#define ${point}`);
    expect(at, point).toBeGreaterThan(source.indexOf('#include<pbrBlockNormalFinal>'));
    expect(at, point).toBeGreaterThan(source.indexOf('vec3 surfaceAlbedo=albedoOpacityOut.surfaceAlbedo;'));
    expect(source.indexOf('#include<pbrBlockNormalFinal>')).toBeGreaterThan(source.indexOf('void main'));
  });

  it('the smoothness override follows the extra map', () => {
    const glsl = code.CUSTOM_FRAGMENT_UPDATE_METALLICROUGHNESS;
    expect(glsl.indexOf('pkIrid2.y')).toBeGreaterThan(glsl.indexOf('pkExtraSampler'));
    expect(glsl).toContain('metallicRoughness.y = 1.0 - pkIrid2.y');
  });

  it('a ramp turns iridescence on, with its ramp and mask samplers', () => {
    const plugin = { ramp: {}, mask: null } as unknown as PkAnimalPlugin;
    const defines: Record<string, boolean> = {};
    PkAnimalPlugin.prototype.prepareDefines.call(plugin, defines as never);
    expect(defines.PK_IRID).toBe(true);
    expect(defines.PK_IRID_MASK).toBe(false);
    PkAnimalPlugin.prototype.prepareDefines.call({ ramp: null, mask: null } as never, defines as never);
    expect(defines.PK_IRID).toBe(false);
    const samplers: string[] = [];
    PkAnimalPlugin.prototype.getSamplers.call(plugin, samplers);
    expect(samplers).toEqual(expect.arrayContaining(['pkRampSampler', 'pkMaskSampler']));
    expect(Object.values(code).join('\n')).toContain('pkRampSampler');
  });

  it('declares the iridescence uniforms', () => {
    const names = PkAnimalPlugin.prototype.getUniforms.call({}).ubo!.map((u) => u.name);
    expect(names).toEqual(expect.arrayContaining(['pkIrid', 'pkIrid2', 'pkMaskSt']));
  });

  it('a map that failed to load does not keep the animal from being drawn', () => {
    const failed = { isReady: () => false, isReadyOrNotBlocking: () => true };
    const ready = PkAnimalPlugin.prototype.isReadyForSubMesh.call({ extra: failed, pattern: null, ramp: failed, mask: null } as never);
    expect(ready).toBe(true);
  });
});
