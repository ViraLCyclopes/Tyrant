import { describe, expect, it } from 'vitest';
import { babylonFaces, linkedFile, loadParts } from './viewerFiles';

const glbFile = 'D:\\ws\\cache\\previews\\b\\acro\\model\\Acro_LOD00.glb';
const glbUrl = `http://asset.localhost/${encodeURIComponent(glbFile)}`;

describe('linkedFile', () => {
  it('maps a texture the .glb links beside it to the file there', () => {
    expect(linkedFile('http://asset.localhost/textures/T_Acro_D.png', glbUrl, glbFile)).toBe(
      'D:\\ws\\cache\\previews\\b\\acro\\model\\textures\\T_Acro_D.png',
    );
  });

  it('decodes escaped names', () => {
    expect(linkedFile('http://asset.localhost/textures/T%20Acro.png', glbUrl, glbFile)).toBe(
      'D:\\ws\\cache\\previews\\b\\acro\\model\\textures\\T Acro.png',
    );
  });

  it('leaves the .glb itself, other sites and folder escapes alone', () => {
    expect(linkedFile(glbUrl, glbUrl, glbFile)).toBeNull();
    expect(linkedFile('https://example.com/textures/x.png', glbUrl, glbFile)).toBeNull();
    expect(linkedFile('http://asset.localhost/textures/%2E%2E%5C..%5Csecret.png', glbUrl, glbFile)).toBeNull();
    expect(linkedFile('http://asset.localhost/C%3A%5CWindows%5Cx.png', glbUrl, glbFile)).toBeNull();
  });
});

describe('babylonFaces', () => {
  it("reorders Unity's cube faces into Babylon's order", () => {
    expect(babylonFaces(['+x', '-x', '+y', '-y', '+z', '-z'])).toEqual(['+x', '+y', '+z', '-x', '-y', '-z']);
  });
});

describe('loadParts', () => {
  it('one failed part leaves the others and is reported by file name', async () => {
    const files = [{ file: 'D:\\p\\Body.glb', url: 'u1' }, { file: 'D:\\p\\Eyes.glb', url: 'u2' }];
    const result = await loadParts(files, async (f) => {
      if (f.url === 'u2') throw new Error('bad accessor');
      return f.url;
    });
    expect(result).toEqual({ loaded: ['u1'], failures: ['Eyes.glb: bad accessor'] });
  });
});
