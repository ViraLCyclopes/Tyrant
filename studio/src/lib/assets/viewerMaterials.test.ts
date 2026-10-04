import { describe, expect, it } from 'vitest';
import type { ModSampledColors, PreviewMaterial } from '$lib/rpc/types.gen';
import { animalUniforms, planMaterial } from './viewerMaterials';

const slot = (name: string, file: string | null = `D:\\p\\textures\\${name}.png`) => ({ name, texture: name, file });

const animal: PreviewMaterial = {
  name: 'Stego', baseColor: 'T_Stego_D', normal: 'T_Stego_N', skinnable: true, shader: 'AnimalShader', animal: true, cutoff: 0.5,
  slots: [slot('_AdultDiffuse'), slot('_AdultNormal'), slot('_AdultExtraMap'), slot('_AdultPatternMask')],
};

describe('planMaterial', () => {
  it('an animal gets its extra and pattern maps and the 0.5 cutout', () => {
    expect(planMaterial(animal)).toEqual({
      name: 'Stego', kind: 'animal', cutoff: 0.5, extra: 'D:\\p\\textures\\_AdultExtraMap.png', pattern: 'D:\\p\\textures\\_AdultPatternMask.png',
    });
  });

  it('an animal without a pattern map gets no pattern step', () => {
    const carch = { ...animal, slots: animal.slots!.filter((s) => s.name !== '_AdultPatternMask') };
    expect(planMaterial(carch)).toMatchObject({ kind: 'animal', pattern: null });
  });

  it('a scenery material keeps its own textures, cut out only when it says so', () => {
    const fence: PreviewMaterial = { name: 'AridClayFence_Mat', baseColor: 'D', normal: 'N', skinnable: false, shader: null, animal: false, cutoff: null, slots: [slot('_DiffuseTex')] };
    expect(planMaterial(fence)).toEqual({ name: 'AridClayFence_Mat', kind: 'standard', cutoff: null, extra: null, pattern: null });
    expect(planMaterial({ ...fence, cutoff: 0.3 }).cutoff).toBe(0.3);
  });

  it('a material without textures is plain, not blank', () => {
    const bare: PreviewMaterial = { name: 'Eyes', baseColor: null, normal: null, skinnable: false };
    expect(planMaterial(bare)).toEqual({ name: 'Eyes', kind: 'standard', cutoff: null, extra: null, pattern: null });
  });
});

describe('animalUniforms', () => {
  it('without colours the textures are left alone', () => {
    expect(animalUniforms(null).flags).toEqual([0, 0, 0, 0]);
  });

  it('turns sampled colours into shader inputs', () => {
    const colors: ModSampledColors = { a: '#ff0000', b: '#0000ff', secondary: null, eye: '#00ff00', strength: 0.8, softness: 0.25, hue: 0.1, saturation: 0, value: -0.2 };
    const u = animalUniforms(colors);
    expect(u.colorA).toEqual([1, 0, 0]);
    expect(u.colorB).toEqual([0, 0, 1]);
    expect(u.eye).toEqual([0, 1, 0]);
    expect(u.flags).toEqual([1, 0, 1, 0.8]);
    expect(u.shape).toEqual([0.25, 0.1, 0, -0.2]);
  });

  it('one colour alone is used for both ends of the pattern', () => {
    const u = animalUniforms({ a: null, b: '#0000ff', secondary: null, eye: null, strength: 1, softness: 0.25, hue: 0, saturation: 0, value: 0 });
    expect(u.colorA).toEqual([0, 0, 1]);
    expect(u.colorB).toEqual([0, 0, 1]);
  });
});
