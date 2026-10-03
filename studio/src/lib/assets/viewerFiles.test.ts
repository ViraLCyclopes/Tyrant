import { describe, expect, it } from 'vitest';
import { babylonFaces, linkedFile } from './viewerFiles';

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
