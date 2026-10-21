import { describe, expect, it } from 'vitest';
import { applyPresetBlock, emptyIridescence, parseFurJson, parseIridescenceJson, parseMaterial, replaceMaterial, toMaterialsJson, type MaterialModel } from './materials';

/** Parses one material block and writes it back. */
const roundTrip = (block: unknown) => toMaterialsJson('Titanoboa', parseMaterial(JSON.stringify({ Titanoboa: block }), 'Titanoboa'));
const same = (block: unknown) => expect(JSON.parse(roundTrip(block) ?? 'null')).toEqual({ Titanoboa: block });

describe('materials', () => {
  it('round-trips the spec example', () => {
    same({
      iridescence: {
        strength: { normal: 0.35, albino: 0.2 },
        variation: 0.15,
        ramp: { thinFilm: { thickness: 420 } },
        mask: { from: 'extra', channel: 'g', invert: false },
        tiling: [2, 2],
        offset: [0, 0],
        scale: -0.25,
        power: -0.3,
        bias: 1.3,
      },
      settings: { _Smoothness: 0.55, _FresnelColor: '#40c0ff', _DetailScale: [4, 4] },
    });
  });

  it('round-trips every ramp form', () => {
    same({ iridescence: { ramp: 'materials/skins/blue/iridescence-ramp.png' } });
    same({ iridescence: { ramp: { stops: ['#3060ff', '#ff60c0'] } } });
    same({ iridescence: { ramp: { thinFilm: { thickness: 500, index: 1.4 } } } });
    same({ iridescence: { strength: 0.3 } }); // no ramp: what lies below (else the game's)
    expect(parseMaterial('{"T":{"iridescence":{"strength":0.3}}}', 'T').iridescence?.ramp).toEqual({ kind: 'inherit' });
    expect(parseMaterial('{"T":{"iridescence":{"ramp":{"thinFilm":{"thickness":420}}}}}', 'T').iridescence?.ramp).toEqual({ kind: 'thinFilm', thickness: 420, index: 1.56 });
  });

  it('round-trips every mask form', () => {
    same({ iridescence: { mask: 'everywhere' } });
    same({ iridescence: { mask: 'materials/skins/blue/iridescence-mask.png' } });
    same({ iridescence: { mask: { from: 'pattern', channel: 'a', invert: true } } });
    expect(parseMaterial('{"T":{"iridescence":{"mask":{"from":"fur"}}}}', 'T').iridescence?.mask).toEqual({ kind: 'from', slot: 'fur', channel: 'r', invert: false });
    expect(parseMaterial('{"T":{"iridescence":{"strength":1}}}', 'T').iridescence?.mask).toEqual({ kind: 'inherit' });
  });

  it('round-trips the explicit "game" ramp, mask and fur masks', () => {
    same({ iridescence: { ramp: 'game', mask: 'game' } });
    same({ fur: { mask: 'game', infantMask: 'game' } });
    expect(parseMaterial('{"T":{"iridescence":{"ramp":"game","mask":"game"}}}', 'T').iridescence).toMatchObject({ ramp: { kind: 'game' }, mask: { kind: 'game' } });
    expect(parseFurJson('{"grow":true,"mask":"game"}')).toEqual({ grow: true, mask: 'game' });
    expect(parseFurJson(null)).toBeNull();
  });

  it('round-trips fur grow on its own, as false, and beside a mask', () => {
    same({ fur: { grow: true } });
    same({ fur: { grow: false } }); // false is kept: it overrides a lower layer that grows fur
    same({ fur: { mask: 'materials/skins/blue/fur-mask.png', grow: true } });
    expect(parseMaterial('{"T":{"fur":{"grow":true}}}', 'T').fur).toEqual({ grow: true });
    expect(parseMaterial('{"T":{"fur":{"grow":"yes"}}}', 'T').fur).toBeUndefined();
    expect(toMaterialsJson('T', { iridescence: null, settings: {}, fur: {} })).toBeNull();
  });

  it('writes a per-variant strength that is the same for every variant as one number', () => {
    const model: MaterialModel = { iridescence: { ...emptyIridescence(), strength: { normal: 0.4, albino: 0.4, leucistic: 0.4 } }, settings: {} };
    expect(toMaterialsJson('T', model)).toBe('{"T":{"iridescence":{"strength":0.4}}}');
    model.iridescence!.strength = { normal: 0.4, albino: 0.4, melanistic: 0.1 };
    expect(toMaterialsJson('T', model)).toBe('{"T":{"iridescence":{"strength":{"normal":0.4,"melanistic":0.1}}}}');
  });

  it('an empty model is null', () => {
    expect(toMaterialsJson('T', { iridescence: null, settings: {} })).toBeNull();
    expect(toMaterialsJson('T', { iridescence: emptyIridescence(), settings: {} })).toBeNull();
    expect(toMaterialsJson('T', parseMaterial(null, 'T'))).toBeNull();
  });

  it('ignores keys it does not know, and other materials', () => {
    const model = parseMaterial('{"T":{"iridescence":{"strength":0.5,"glow":3},"shine":1,"settings":{"_Smoothness":0.5}},"Other":{"settings":{"_Cutoff":0.2}}}', 'T');
    expect(toMaterialsJson('T', model)).toBe('{"T":{"iridescence":{"strength":0.5},"settings":{"_Smoothness":0.5}}}');
  });

  it("replacing one material keeps the mod's other materials", () => {
    const saved = '{"Other":{"settings":{"_Cutoff":0.2}},"T":{"settings":{"_Smoothness":0.5}}}';
    expect(JSON.parse(replaceMaterial(saved, 'T', { iridescence: null, settings: { _Smoothness: 0.6 } })!)).toEqual({ Other: { settings: { _Cutoff: 0.2 } }, T: { settings: { _Smoothness: 0.6 } } });
    expect(replaceMaterial('{"T":{"settings":{"_Smoothness":0.5}}}', 'T', { iridescence: null, settings: {} })).toBeNull();
  });

  it('round-trips a ramp and a mask taken from a species', () => {
    same({ iridescence: { ramp: { species: 'Microraptor' }, mask: { species: 'Oviraptor' } } });
    expect(parseMaterial('{"T":{"iridescence":{"ramp":{"species":"Microraptor"}}}}', 'T').iridescence?.ramp).toEqual({ kind: 'species', species: 'Microraptor' });
    expect(parseMaterial('{"T":{"iridescence":{"mask":{"species":""}}}}', 'T').iridescence?.mask).toEqual({ kind: 'inherit' }); // no id: dropped
  });

  it('a preset block replaces what it sets and keeps everything else', () => {
    const model = parseMaterial(
      JSON.stringify({ T: { iridescence: { strength: 0.6, variation: 0.3, ramp: { thinFilm: { thickness: 300 } }, tiling: [2, 2] }, settings: { _Smoothness: 0.4, _Length: 1 }, fur: { mask: 'f.png' } } }),
      'T',
    );
    applyPresetBlock(model, '{"iridescence":{"strength":{"normal":0.25,"albino":0.1},"ramp":{"species":"Microraptor"},"mask":{"species":"Microraptor"},"scale":-0.25,"power":-0.3,"bias":1.3}}');
    expect(JSON.parse(toMaterialsJson('T', model)!)).toEqual({
      T: {
        iridescence: { strength: { normal: 0.25, albino: 0.1 }, variation: 0.3, ramp: { species: 'Microraptor' }, mask: { species: 'Microraptor' }, tiling: [2, 2], scale: -0.25, power: -0.3, bias: 1.3 },
        settings: { _Smoothness: 0.4, _Length: 1 },
        fur: { mask: 'f.png' },
      },
    });
    applyPresetBlock(model, '{"fur":{"grow":true},"settings":{"_Length":1.5,"_DroopDirection":[0,-10.3,8.11]}}');
    expect(model.fur).toEqual({ mask: 'f.png', grow: true });
    expect(model.settings).toEqual({ _Smoothness: 0.4, _Length: 1.5, _DroopDirection: [0, -10.3, 8.11] });
    expect(model.iridescence?.ramp).toEqual({ kind: 'species', species: 'Microraptor' }); // the fur preset leaves the iridescence alone
  });

  it('a preset block without a strength turns iridescence on but keeps a strength this level has', () => {
    const kept = parseMaterial('{"T":{"iridescence":{"strength":{"normal":0.6,"albino":0.2}}}}', 'T');
    applyPresetBlock(kept, '{"iridescence":{"ramp":{"species":"Edmontosaurus"},"mask":"everywhere"}}');
    expect(kept.iridescence?.strength).toEqual({ normal: 0.6, albino: 0.2 });
    const off = parseMaterial('{"T":{"iridescence":{"strength":{"normal":0,"albino":0}}}}', 'T');
    applyPresetBlock(off, '{"iridescence":{"ramp":{"species":"Edmontosaurus"}}}');
    expect(off.iridescence?.strength).toEqual({ normal: 1, albino: 1 }); // off everywhere: on, as the game's preset does
    const none = parseMaterial(null, 'T');
    applyPresetBlock(none, '{"iridescence":{"ramp":{"species":"Edmontosaurus"}}}');
    expect(none.iridescence?.strength).toEqual({ normal: 1 });
  });

  it('round-trips a baby strength and a fur age', () => {
    same({ iridescence: { strength: 0, babyStrength: 0.3 }, fur: { age: 'adults' } });
    same({ iridescence: { babyStrength: 0 } }); // 0 is kept: it overrides a lower layer's baby shimmer
    same({ fur: { mask: 'materials/skins/blue/fur-mask.png', grow: true, age: 'both' } });
    expect(parseMaterial('{"T":{"iridescence":{"babyStrength":0.4}}}', 'T').iridescence).toMatchObject({ babyStrength: 0.4, strength: null });
    expect(parseMaterial('{"T":{"iridescence":{"strength":0.4}}}', 'T').iridescence?.babyStrength).toBeNull();
    expect(parseFurJson('{"age":"babies"}')).toEqual({ age: 'babies' });
    expect(parseMaterial('{"T":{"fur":{"age":"teens"}}}', 'T').fur).toBeUndefined(); // not an age: dropped
    expect(parseIridescenceJson('{"babyStrength":0.2}')?.babyStrength).toBe(0.2);
  });

  it('reads an inherited iridescence block', () => {
    expect(parseIridescenceJson('{"strength":0.2,"scale":-1}')).toMatchObject({ strength: { normal: 0.2 }, scale: -1, power: null });
    expect(parseIridescenceJson(null)).toBeNull();
  });
});
