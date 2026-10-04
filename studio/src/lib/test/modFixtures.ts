import type { ModDetail } from '$lib/rpc/types.gen';

/** A mod as mods.get returns it: two Carcharodontosaurus skins and one texture replacement. */
export function modDetail(over: Partial<ModDetail> = {}): ModDetail {
  return {
    id: 'red-spot',
    name: 'Carch pack',
    version: '1.0.0',
    author: null,
    description: null,
    dir: 'D:\\ws\\mods\\red-spot',
    revision: 'r1',
    manifestJson: '{"name":"Carch pack"}',
    replace: [{ texture: 'T_carch_alt1_male_D', key: null, guid: null, file: 'textures/T_carch_alt1_male_D.png' }],
    skins: [
      {
        id: 'blue', key: 'red-spot/blue', species: 'Carcharodontosaurus', name: 'Blue-green stripes', base: 'Alt 1', thumbnail: null,
        male: { diffuse: 'skins/blue/male_D.png' }, female: null, colorsJson: null, baseMaleSlots: ['diffuse', 'normal', 'pattern'], baseFemaleSlots: ['diffuse'],
      },
      {
        id: 'red', key: 'red-spot/red', species: 'Carcharodontosaurus', name: 'Red spot', base: 'Base', thumbnail: null,
        male: { diffuse: 'skins/red/male_D.png' }, female: null, colorsJson: null, baseMaleSlots: null, baseFemaleSlots: null,
      },
    ],
    ...over,
  };
}
