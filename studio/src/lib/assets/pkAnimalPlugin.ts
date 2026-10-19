import { MaterialPluginBase, type BaseTexture, type MaterialDefines, type Nullable, type PBRMaterial, type UniformBuffer } from '@babylonjs/core';
import { animalUniforms, iridescenceUniforms, type AnimalUniforms, type IridescenceUniforms } from './viewerMaterials';

/**
 * The game's animal shader, close but not exact (the same rules as ColorPreview in the core, which the 2D strip uses): extra G = ambient occlusion, extra R = smoothness (roughness = 1 − R,
 * never metallic), extra R above 0.9 = the eyes (eye colour); pattern R blends colour A → B (sat(R / softness)) by
 * strength, pattern G takes the secondary colour, and the hue/saturation/value tint applies where pattern R > 0.
 * Iridescence is the game's deferred add, `saturate((1 - (scale·(1 − N·V)^power + bias)) · ramp(N·V) · strength · mask(uv·ST))`
 * on the gamma-space albedo, with the normal-mapped normal (the game's ramp lookup uses the geometric one). A material
 * smoothness multiplies the extra map's. Lighting, rim and fur are Babylon's own.
 */
export class PkAnimalPlugin extends MaterialPluginBase {
  extra: Nullable<BaseTexture> = null;
  pattern: Nullable<BaseTexture> = null;
  uniforms: AnimalUniforms = animalUniforms(null);
  /** The iridescence ramp (sampled at N·V, N·V) and mask; no ramp, no iridescence; no mask, everywhere. */
  ramp: Nullable<BaseTexture> = null;
  mask: Nullable<BaseTexture> = null;
  iridescence: IridescenceUniforms = iridescenceUniforms(null);

  constructor(material: PBRMaterial) {
    super(material, 'PkAnimal', 200, { PK_EXTRA: false, PK_PATTERN: false, PK_IRID: false, PK_IRID_MASK: false });
    this._enable(true);
  }

  override getClassName(): string {
    return 'PkAnimalPlugin';
  }

  /** Like PBR's own textures: a map that failed to load does not block drawing (it is simply not applied). */
  override isReadyForSubMesh(): boolean {
    return [this.extra, this.pattern, this.ramp, this.mask].every((map) => !map || map.isReadyOrNotBlocking());
  }

  override prepareDefines(defines: MaterialDefines): void {
    const flags = defines as unknown as Record<string, boolean>;
    flags.PK_EXTRA = !!this.extra;
    flags.PK_PATTERN = !!this.pattern;
    flags.PK_IRID = !!this.ramp;
    flags.PK_IRID_MASK = !!this.ramp && !!this.mask;
  }

  override getSamplers(samplers: string[]): void {
    samplers.push('pkExtraSampler', 'pkPatternSampler', 'pkRampSampler', 'pkMaskSampler');
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
        { name: 'pkIrid', size: 4, type: 'vec4' },
        { name: 'pkIrid2', size: 4, type: 'vec4' },
        { name: 'pkMaskSt', size: 4, type: 'vec4' },
      ],
      fragment:
        'uniform vec3 pkColorA; uniform vec3 pkColorB; uniform vec3 pkSecondary; uniform vec3 pkEye; uniform vec4 pkFlags; uniform vec4 pkShape;' +
        ' uniform vec4 pkIrid; uniform vec4 pkIrid2; uniform vec4 pkMaskSt;',
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
    const i = this.iridescence;
    // on, strength, scale, power; bias, smoothness multiplier (−1: the extra map's alone)
    ubo.updateFloat4('pkIrid', i.on ? 1 : 0, i.strength, i.shaping[0], i.shaping[1]);
    ubo.updateFloat4('pkIrid2', i.shaping[2], i.smoothness ?? -1, 0, 0);
    ubo.updateFloat4('pkMaskSt', ...i.maskSt);
    if (this.extra) ubo.setTexture('pkExtraSampler', this.extra);
    if (this.pattern) ubo.setTexture('pkPatternSampler', this.pattern);
    if (this.ramp) ubo.setTexture('pkRampSampler', this.ramp);
    if (this.ramp && this.mask) ubo.setTexture('pkMaskSampler', this.mask);
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
        #ifdef PK_IRID
          uniform sampler2D pkRampSampler;
        #endif
        #ifdef PK_IRID_MASK
          uniform sampler2D pkMaskSampler;
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
          #if defined(PK_PATTERN) && defined(MAINUV1)
            vec4 pkPattern = texture2D(pkPatternSampler, vMainUV1);
            if (pkPattern.r > 0.0) {
              pkColor = pkShift(pkColor, pkShape.y, pkShape.z, pkShape.w);
              if (pkFlags.x > 0.5) pkColor = mix(pkColor, mix(pkColorA, pkColorB, clamp(pkPattern.r / pkShape.x, 0.0, 1.0)), pkStrength);
              if (pkFlags.y > 0.5 && pkPattern.g > 0.0) pkColor = mix(pkColor, pkSecondary, pkPattern.g * pkStrength);
            }
          #endif
          #if defined(PK_EXTRA) && defined(MAINUV1)
            if (pkFlags.z > 0.5 && texture2D(pkExtraSampler, vMainUV1).r > 0.9) pkColor = mix(pkColor, pkEye, pkStrength);
          #endif
          surfaceAlbedo = toLinearSpace(pkColor);
        }
      `,
      CUSTOM_FRAGMENT_UPDATE_METALLICROUGHNESS: `
        #if defined(PK_EXTRA) && defined(MAINUV1)
          metallicRoughness = vec2(0.0, 1.0 - texture2D(pkExtraSampler, vMainUV1).r);
          if (pkIrid2.y >= 0.0) metallicRoughness.y = 1.0 - clamp(texture2D(pkExtraSampler, vMainUV1).r * pkIrid2.y, 0.0, 1.0);
        #else
          if (pkIrid2.y >= 0.0) metallicRoughness.y = 1.0 - clamp(pkIrid2.y, 0.0, 1.0);
        #endif
      `,
      // Iridescence needs the normal and the view direction, which CUSTOM_FRAGMENT_UPDATE_ALBEDO (inside albedoOpacityBlock)
      // cannot see; this is the first point in main after the albedo. The add is in gamma space, like the colours there.
      CUSTOM_FRAGMENT_UPDATE_ALPHA: `
        #ifdef PK_IRID
          if (pkIrid.x > 0.5) {
            // Below 1: the game's powers are negative, and pow(0.0, negative) is undefined in GLSL (HLSL gives infinity, then 1 after saturate; so does this).
            float nv = clamp(dot(normalW, viewDirectionW), 0.0, 0.9999);
            float fall = clamp(1.0 - (pkIrid.z * pow(1.0 - nv, pkIrid.w) + pkIrid2.x), 0.0, 1.0);
            vec3 pkIridMask = vec3(1.0);
            #if defined(PK_IRID_MASK) && defined(MAINUV1)
              pkIridMask = texture2D(pkMaskSampler, vMainUV1 * pkMaskSt.xy + pkMaskSt.zw).rgb;
            #endif
            vec3 irid = clamp(fall * texture2D(pkRampSampler, vec2(nv, nv)).rgb * pkIrid.y * pkIridMask, 0.0, 1.0);
            surfaceAlbedo = toLinearSpace(toGammaSpace(surfaceAlbedo) + irid);
          }
        #endif
      `,
      // After the ambient occlusion block has run (aoOut is declared just after CUSTOM_FRAGMENT_BEFORE_LIGHTS).
      '!aoOut=ambientOcclusionBlock\\([^;]*\\);': `$0
        #if defined(PK_EXTRA) && defined(MAINUV1)
          aoOut.ambientOcclusionColor *= texture2D(pkExtraSampler, vMainUV1).g;
        #endif
      `,
    };
  }
}
