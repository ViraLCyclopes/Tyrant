/**
 * A .glb links its textures as "textures/<name>.png" beside it. Babylon resolves that against the .glb's URL, which on
 * the asset protocol is one escaped path segment, so it asks for http://asset.localhost/textures/…. This maps such a
 * request back to the file beside the .glb; null for anything else (the .glb itself, other sites, folder escapes).
 */
export function linkedFile(requestedUrl: string, glbUrl: string, glbFile: string): string | null {
  const base = glbUrl.slice(0, glbUrl.lastIndexOf('/') + 1);
  if (requestedUrl === glbUrl || !requestedUrl.startsWith(base)) return null;
  let relative: string;
  try {
    relative = decodeURIComponent(requestedUrl.slice(base.length));
  } catch {
    return null;
  }
  const parts = relative.split(/[\\/]/);
  if (parts.some((part) => part === '' || part === '.' || part === '..' || part.includes(':'))) return null;
  const dir = glbFile.slice(0, Math.max(glbFile.lastIndexOf('\\'), glbFile.lastIndexOf('/')) + 1);
  return dir + parts.join('\\');
}

/** Babylon wants cube faces as +X, +Y, +Z, -X, -Y, -Z; the core writes Unity's +X, -X, +Y, -Y, +Z, -Z. */
export function babylonFaces<T>(unity: T[]): T[] {
  return [unity[0], unity[2], unity[4], unity[1], unity[3], unity[5]];
}

/** Loads every part; a part that fails is reported by file name and the rest are kept (C1). */
export async function loadParts<T>(files: { file: string; url: string }[], load: (f: { file: string; url: string }) => Promise<T>): Promise<{ loaded: T[]; failures: string[] }> {
  const loaded: T[] = [];
  const failures: string[] = [];
  for (const f of files) {
    try {
      loaded.push(await load(f));
    } catch (e) {
      failures.push(`${f.file.split(/[\\/]/).pop()}: ${e instanceof Error ? e.message : String(e)}`);
    }
  }
  return { loaded, failures };
}
