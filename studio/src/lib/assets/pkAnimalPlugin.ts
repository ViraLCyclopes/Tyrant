import { MaterialPluginBase, type BaseTexture, type MaterialDefines, type Nullable, type PBRMaterial, type UniformBuffer } from '@babylonjs/core';
import { animalUniforms, type AnimalUniforms } from './viewerMaterials';

/**
 * The game's animal shader, close but not exact (docs/superpowers/specs/2026-10-03-tyrant-skin-colors-design.md §2, and
 * ColorPreview in the core, which the 2D strip uses): extra G = ambient occlusion, extra R = smoothness (roughness = 1 − R,
 * never metallic), extra R above 0.9 = the eyes (eye colour); pattern R blends colour A → B (sat(R / softness)) by
 * strength, pattern G takes the secondary colour, and the hue/saturation/value tint applies where pattern R > 0.
 * Lighting, rim and fur are Babylon's own.
 */
export class PkAnimalPlugin extends MaterialPluginBase {
  extra: Nullable<BaseTexture> = null;
  pattern: Nullable<BaseTexture> = null;
  uniforms: AnimalUniforms = animalUniforms(null);

  constructor(material: PBRMaterial) {
    super(material, 'PkAnimal', 200, { PK_EXTRA: false, PK_PATTERN: false });
    this._enable(true);
  }

  override getClassName(): string {
    return 'PkAnimalPlugin';
  }

  override isReadyForSubMesh(): boolean {
    return (!this.extra || this.extra.isReady()) && (!this.pattern || this.pattern.isReady());
  }

  override prepareDefines(defines: MaterialDefines): void {
    const flags = defines as unknown as Record<string, boolean>;
    flags.PK_EXTRA = !!this.extra;
    flags.PK_PATTERN = !!this.pattern;
  }

  override getSamplers(samplers: string[]): void {
    samplers.push('pkExtraSampler', 'pkPatternSampler');
  }

  override getUniforms() {
    return {
      ubo: [
        { name: 'pkColorA', size: 3, type: 'vec3' },
        { name: 'pkColorB', size: 3, type: 'vec3' },
        { name: 'pkSecondary', size: 3, type: 'vec3' },
        { name: 'pkEye', size: 3, type: 'vec3' },
        { name: 'pkFlags', size: 4, type: 'vec4' },
        { name: 'pkShape', size: 4, type: 'vec4' },
      ],
      fragment: 'uniform vec3 pkColorA; uniform vec3 pkColorB; uniform vec3 pkSecondary; uniform vec3 pkEye; uniform vec4 pkFlags; uniform vec4 pkShape;',
    };
  }

  override bindForSubMesh(ubo: UniformBuffer): void {
    const u = this.uniforms;
    ubo.updateFloat3('pkColorA', ...u.colorA);
    ubo.updateFloat3('pkColorB', ...u.colorB);
    ubo.updateFloat3('pkSecondary', ...u.secondary);
    ubo.updateFloat3('pkEye', ...u.eye);
    ubo.updateFloat4('pkFlags', ...u.flags);
    ubo.updateFloat4('pkShape', ...u.shape);
    if (this.extra) ubo.setTexture('pkExtraSampler', this.extra);
    if (this.pattern) ubo.setTexture('pkPatternSampler', this.pattern);
  }

  override getCustomCode(shaderType: string): Nullable<{ [pointName: string]: string }> {
    if (shaderType !== 'fragment') return null;
    return {
      CUSTOM_FRAGMENT_DEFINITIONS: `
        #ifdef PK_EXTRA
          uniform sampler2D pkExtraSampler;
        #endif
        #ifdef PK_PATTERN
          uniform sampler2D pkPatternSampler;
        #endif
        vec3 pkShift(vec3 c, float hue, float sat, float val) {
          if (hue == 0.0 && sat == 0.0 && val == 0.0) return c;
          vec4 K = vec4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
          vec4 p = mix(vec4(c.bg, K.wz), vec4(c.gb, K.xy), step(c.b, c.g));
          vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));
          float d = q.x - min(q.w, q.y);
          vec3 hsv = vec3(abs(q.z + (q.w - q.y) / (6.0 * d + 1e-10)), d / (q.x + 1e-10), q.x);
          hsv.x = fract(hsv.x + hue);
          hsv.y = clamp(hsv.y + sat, 0.0, 1.0);
          hsv.z = clamp(hsv.z + val, 0.0, 1.0);
          vec3 rgb = clamp(abs(mod(hsv.x * 6.0 + vec3(0.0, 4.0, 2.0), 6.0) - 3.0) - 1.0, 0.0, 1.0);
          return hsv.z * mix(vec3(1.0), rgb, hsv.y);
        }
      `,
      CUSTOM_FRAGMENT_UPDATE_ALBEDO: `
        {
          vec3 pkColor = toGammaSpace(surfaceAlbedo);
          float pkStrength = pkFlags.w;
          #ifdef PK_PATTERN
            vec4 pkPattern = texture2D(pkPatternSampler, vMainUV1);
            if (pkPattern.r > 0.0) {
              pkColor = pkShift(pkColor, pkShape.y, pkShape.z, pkShape.w);
              if (pkFlags.x > 0.5) pkColor = mix(pkColor, mix(pkColorA, pkColorB, clamp(pkPattern.r / pkShape.x, 0.0, 1.0)), pkStrength);
              if (pkFlags.y > 0.5 && pkPattern.g > 0.0) pkColor = mix(pkColor, pkSecondary, pkPattern.g * pkStrength);
            }
          #endif
          #ifdef PK_EXTRA
            if (pkFlags.z > 0.5 && texture2D(pkExtraSampler, vMainUV1).r > 0.9) pkColor = mix(pkColor, pkEye, pkStrength);
          #endif
          surfaceAlbedo = toLinearSpace(pkColor);
        }
      `,
      CUSTOM_FRAGMENT_UPDATE_METALLICROUGHNESS: `
        #ifdef PK_EXTRA
          metallicRoughness = vec2(0.0, 1.0 - texture2D(pkExtraSampler, vMainUV1).r);
        #endif
      `,
      CUSTOM_FRAGMENT_BEFORE_LIGHTS: `
        #ifdef PK_EXTRA
          aoOut.ambientOcclusionColor *= texture2D(pkExtraSampler, vMainUV1).g;
        #endif
      `,
    };
  }
}
